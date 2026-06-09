using System.Globalization;
using System.Text.RegularExpressions;
using Cel2Sql.Errors;

namespace Cel2Sql.Dialects.Postgres;

/// <summary>
/// Converts RE2 regex patterns to POSIX ERE format for PostgreSQL.
/// Performs security validation to prevent ReDoS attacks (CWE-1333).
///
/// <para>Ported from the Go <c>dialect/postgres/regex.go</c> implementation.</para>
/// </summary>
internal static class PostgresRegex
{
    /// <summary>Maximum allowed regex pattern length.</summary>
    internal const int MaxPatternLength = 500;

    /// <summary>Maximum allowed capture groups in a pattern.</summary>
    internal const int MaxGroups = 20;

    /// <summary>Maximum allowed nesting depth of parenthesized groups.</summary>
    internal const int MaxNestingDepth = 10;

    private static readonly Regex NestedQuantifiers = new("[*+][*+]", RegexOptions.Compiled);
    private static readonly Regex QuantifiedAlternation = new(@"\([^)]*\|[^)]*\)[*+]", RegexOptions.Compiled);

    /// <summary>
    /// Converts an RE2 regex pattern to POSIX ERE format for PostgreSQL.
    ///
    /// <para>Security checks performed (in order):
    /// <list type="number">
    ///   <item>Pattern length limit</item>
    ///   <item>Extract <c>(?i)</c> flag</item>
    ///   <item>Detect unsupported features: lookahead, lookbehind, named groups, inline flags</item>
    ///   <item>Detect catastrophic nested quantifiers</item>
    ///   <item>Count and limit capture groups</item>
    ///   <item>Detect exponential alternation</item>
    ///   <item>Check nesting depth</item>
    /// </list></para>
    /// </summary>
    /// <param name="re2Pattern">The RE2 regex pattern.</param>
    /// <returns>A <see cref="RegexResult"/> with the POSIX pattern and case sensitivity flag.</returns>
    /// <exception cref="ConversionException">If the pattern is invalid or contains unsupported features.</exception>
    internal static RegexResult ConvertRe2ToPosix(string re2Pattern)
    {
        // 1. Check pattern length
        if (re2Pattern.Length > MaxPatternLength)
        {
            throw new ConversionException(
                "Invalid pattern in expression",
                string.Format(CultureInfo.InvariantCulture,
                    "pattern length {0} exceeds limit of {1} characters",
                    re2Pattern.Length, MaxPatternLength));
        }

        // 2. Extract case-insensitive flag
        bool caseInsensitive = false;
        string pattern = re2Pattern;
        if (pattern.StartsWith("(?i)", StringComparison.Ordinal))
        {
            caseInsensitive = true;
            pattern = pattern.Substring(4);
        }

        // 3. Detect unsupported features
        if (pattern.Contains("(?=", StringComparison.Ordinal) || pattern.Contains("(?!", StringComparison.Ordinal))
        {
            throw new ConversionException(
                "Invalid pattern in expression",
                "lookahead assertions (?=...), (?!...) are not supported in PostgreSQL POSIX regex");
        }
        if (pattern.Contains("(?<=", StringComparison.Ordinal) || pattern.Contains("(?<!", StringComparison.Ordinal))
        {
            throw new ConversionException(
                "Invalid pattern in expression",
                "lookbehind assertions (?<=...), (?<!...) are not supported in PostgreSQL POSIX regex");
        }
        if (pattern.Contains("(?P<", StringComparison.Ordinal))
        {
            throw new ConversionException(
                "Invalid pattern in expression",
                "named capture groups (?P<name>...) are not supported in PostgreSQL POSIX regex");
        }
        if (pattern.Contains("(?m", StringComparison.Ordinal) || pattern.Contains("(?s", StringComparison.Ordinal) || pattern.Contains("(?-", StringComparison.Ordinal))
        {
            throw new ConversionException(
                "Invalid pattern in expression",
                "inline flags other than (?i) are not supported in PostgreSQL POSIX regex");
        }

        // 4. Detect catastrophic nested quantifiers
        if (NestedQuantifiers.IsMatch(pattern))
        {
            throw new ConversionException(
                "Invalid pattern in expression",
                "regex contains catastrophic nested quantifiers that could cause ReDoS");
        }

        // Check for groups with quantifiers that are themselves quantified
        ValidateNoNestedQuantifiers(pattern);

        // 5. Count and limit capture groups
        int groupCount = CountUnescapedParens(pattern);
        if (groupCount > MaxGroups)
        {
            throw new ConversionException(
                "Invalid pattern in expression",
                string.Format(CultureInfo.InvariantCulture,
                    "regex contains {0} capture groups, exceeds limit of {1}",
                    groupCount, MaxGroups));
        }

        // 6. Detect exponential alternation patterns
        if (QuantifiedAlternation.IsMatch(pattern))
        {
            throw new ConversionException(
                "Invalid pattern in expression",
                "regex contains quantified alternation that could cause ReDoS");
        }

        // 7. Check nesting depth
        int maxDepth = ComputeMaxNestingDepth(pattern);
        if (maxDepth > MaxNestingDepth)
        {
            throw new ConversionException(
                "Invalid pattern in expression",
                string.Format(CultureInfo.InvariantCulture,
                    "nesting depth {0} exceeds limit of {1}", maxDepth, MaxNestingDepth));
        }

        // 8. Convert RE2 to POSIX
        string posix = pattern;
        posix = posix.Replace("\\b", "\\y");
        posix = posix.Replace("\\B", "[^[:alnum:]_]");
        posix = posix.Replace("\\d", "[[:digit:]]");
        posix = posix.Replace("\\D", "[^[:digit:]]");
        posix = posix.Replace("\\w", "[[:alnum:]_]");
        posix = posix.Replace("\\W", "[^[:alnum:]_]");
        posix = posix.Replace("\\s", "[[:space:]]");
        posix = posix.Replace("\\S", "[^[:space:]]");
        posix = posix.Replace("(?:", "(");

        return new RegexResult(posix, caseInsensitive);
    }

    /// <summary>
    /// Validates that no quantified groups contain inner quantifiers (nested quantifiers).
    /// This detects patterns like <c>(a+)+</c> that can cause catastrophic backtracking.
    /// </summary>
    private static void ValidateNoNestedQuantifiers(string pattern)
    {
        int depth = 0;
        bool[] groupHasQuantifier = new bool[pattern.Length]; // oversized but safe
        int stackTop = -1;

        for (int i = 0; i < pattern.Length; i++)
        {
            char ch = pattern[i];

            // Skip escaped characters
            if (i > 0 && pattern[i - 1] == '\\')
            {
                continue;
            }

            switch (ch)
            {
                case '(':
                    depth++;
                    stackTop++;
                    groupHasQuantifier[stackTop] = false;
                    break;
                case ')':
                    if (depth > 0)
                    {
                        depth--;
                        if (i + 1 < pattern.Length)
                        {
                            char next = pattern[i + 1];
                            if (next == '*' || next == '+' || next == '?' || next == '{')
                            {
                                if (stackTop >= 0 && groupHasQuantifier[stackTop])
                                {
                                    throw new ConversionException(
                                        "Invalid pattern in expression",
                                        "regex contains catastrophic nested quantifiers that could cause ReDoS");
                                }
                            }
                        }
                        if (stackTop > 0)
                        {
                            if (groupHasQuantifier[stackTop])
                            {
                                groupHasQuantifier[stackTop - 1] = true;
                            }
                        }
                        if (stackTop >= 0)
                        {
                            stackTop--;
                        }
                    }
                    break;
                case '*':
                case '+':
                case '?':
                    if (stackTop >= 0)
                    {
                        groupHasQuantifier[stackTop] = true;
                    }
                    break;
                case '{':
                    if (stackTop >= 0)
                    {
                        groupHasQuantifier[stackTop] = true;
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Counts the number of unescaped opening parentheses in the pattern.
    /// </summary>
    private static int CountUnescapedParens(string pattern)
    {
        int count = 0;
        for (int i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '(' && (i == 0 || pattern[i - 1] != '\\'))
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Computes the maximum nesting depth of parenthesized groups in the pattern.
    /// </summary>
    private static int ComputeMaxNestingDepth(string pattern)
    {
        int maxDepth = 0;
        int currentDepth = 0;
        for (int i = 0; i < pattern.Length; i++)
        {
            char ch = pattern[i];
            if (ch == '(' && (i == 0 || pattern[i - 1] != '\\'))
            {
                currentDepth++;
                if (currentDepth > maxDepth)
                {
                    maxDepth = currentDepth;
                }
            }
            else if (ch == ')' && (i == 0 || pattern[i - 1] != '\\'))
            {
                currentDepth--;
            }
        }
        return maxDepth;
    }
}
