using System.Globalization;
using System.Text.RegularExpressions;
using Cel2Sql.Errors;

namespace Cel2Sql.Dialects.MySql;

/// <summary>
/// Converts RE2 regex patterns to MySQL/ICU regex format.
/// Performs security validation to prevent ReDoS attacks (CWE-1333).
///
/// <para>Ported from the Go <c>dialect/mysql/regex.go</c> implementation.</para>
/// </summary>
internal static class MySqlRegex
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
    /// Converts an RE2 regex pattern to MySQL/ICU regex format.
    ///
    /// <para>Security checks performed (in order):
    /// <list type="number">
    ///   <item>Pattern length limit</item>
    ///   <item>Validate pattern compiles as a regex</item>
    ///   <item>Reject lookahead, lookbehind, and named groups</item>
    ///   <item>Detect catastrophic nested quantifiers</item>
    ///   <item>Check nested quantifiers in groups</item>
    ///   <item>Count and limit capture groups</item>
    ///   <item>Detect exponential alternation</item>
    ///   <item>Check nesting depth</item>
    ///   <item>Handle <c>(?i)</c> flag</item>
    ///   <item>Reject other inline flags</item>
    ///   <item>Convert non-capturing groups to plain groups</item>
    /// </list></para>
    ///
    /// <para>MySQL ICU supports <c>\d</c>, <c>\w</c>, <c>\s</c>, <c>\b</c> natively,
    /// so no POSIX class conversion is needed (unlike PostgreSQL).</para>
    /// </summary>
    /// <param name="re2Pattern">The RE2 regex pattern.</param>
    /// <returns>A <see cref="RegexResult"/> with the MySQL pattern and case sensitivity flag.</returns>
    /// <exception cref="ConversionException">If the pattern is invalid or contains unsupported features.</exception>
    internal static RegexResult ConvertRe2ToMySql(string re2Pattern)
    {
        // 1. Check pattern length
        if (re2Pattern.Length > MaxPatternLength)
        {
            throw new ConversionException(
                "Invalid regex pattern",
                string.Format(CultureInfo.InvariantCulture,
                    "pattern length {0} exceeds limit of {1} characters",
                    re2Pattern.Length, MaxPatternLength));
        }

        // 2. Validate pattern compiles
        try
        {
            _ = new Regex(re2Pattern);
        }
        catch (ArgumentException e)
        {
            throw new ConversionException(
                "Invalid regex pattern",
                "regex pattern does not compile: " + e.Message);
        }

        // 3. Reject lookahead, lookbehind, and named groups
        if (re2Pattern.Contains("(?=", StringComparison.Ordinal) || re2Pattern.Contains("(?!", StringComparison.Ordinal))
        {
            throw new ConversionException(
                "Invalid regex pattern",
                "lookahead assertions (?=...), (?!...) are not supported in MySQL regex");
        }
        if (re2Pattern.Contains("(?<=", StringComparison.Ordinal) || re2Pattern.Contains("(?<!", StringComparison.Ordinal))
        {
            throw new ConversionException(
                "Invalid regex pattern",
                "lookbehind assertions (?<=...), (?<!...) are not supported in MySQL regex");
        }
        if (re2Pattern.Contains("(?P<", StringComparison.Ordinal))
        {
            throw new ConversionException(
                "Invalid regex pattern",
                "named capture groups (?P<name>...) are not supported in MySQL regex");
        }

        // 4. Detect catastrophic nested quantifiers
        if (NestedQuantifiers.IsMatch(re2Pattern))
        {
            throw new ConversionException(
                "Invalid regex pattern",
                "regex contains catastrophic nested quantifiers that could cause ReDoS");
        }

        // 5. Check nested quantifiers in groups
        ValidateNoNestedQuantifiers(re2Pattern);

        // 6. Count and limit capture groups
        int groupCount = CountUnescapedParens(re2Pattern);
        if (groupCount > MaxGroups)
        {
            throw new ConversionException(
                "Invalid regex pattern",
                string.Format(CultureInfo.InvariantCulture,
                    "regex contains {0} capture groups, exceeds limit of {1}",
                    groupCount, MaxGroups));
        }

        // 7. Detect exponential alternation patterns
        if (QuantifiedAlternation.IsMatch(re2Pattern))
        {
            throw new ConversionException(
                "Invalid regex pattern",
                "regex contains quantified alternation that could cause ReDoS");
        }

        // 8. Check nesting depth
        int maxDepth = ComputeMaxNestingDepth(re2Pattern);
        if (maxDepth > MaxNestingDepth)
        {
            throw new ConversionException(
                "Invalid regex pattern",
                string.Format(CultureInfo.InvariantCulture,
                    "nesting depth {0} exceeds limit of {1}", maxDepth, MaxNestingDepth));
        }

        // 9. Handle (?i) flag -> set caseInsensitive=true, strip prefix
        bool caseInsensitive = false;
        string pattern = re2Pattern;
        if (pattern.StartsWith("(?i)", StringComparison.Ordinal))
        {
            caseInsensitive = true;
            pattern = pattern.Substring(4);
        }

        // 10. Reject other inline flags
        if (pattern.Contains("(?m", StringComparison.Ordinal) || pattern.Contains("(?s", StringComparison.Ordinal) || pattern.Contains("(?-", StringComparison.Ordinal))
        {
            throw new ConversionException(
                "Invalid regex pattern",
                "inline flags other than (?i) are not supported in MySQL regex");
        }

        // 11. Convert non-capturing groups (?:...) to plain groups (...)
        pattern = pattern.Replace("(?:", "(");

        // MySQL ICU supports \d, \w, \s, \b natively - no conversion needed

        // 13. Return result
        return new RegexResult(pattern, caseInsensitive);
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
                                        "Invalid regex pattern",
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
