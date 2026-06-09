using System.Globalization;
using System.Text.RegularExpressions;
using Cel2Sql.Errors;

namespace Cel2Sql.Dialects.Spark;

/// <summary>
/// Validates and converts RE2 regex patterns for Apache Spark SQL.
/// Spark uses java.util.regex (Java's Pattern engine), which is largely a
/// superset of RE2 for the safe subset cel2sql accepts. After security
/// validation, the pattern passes through unchanged — Spark's regex engine
/// honours inline <c>(?i)</c> natively, so this method always reports
/// <c>caseInsensitive=false</c> and lets the engine handle the flag.
///
/// <para>Ported from the Go <c>dialect/spark/regex.go</c> implementation.
/// The validation logic is shared with the other RE2-style dialects (DuckDB,
/// PostgreSQL) and prevents ReDoS attacks (CWE-1333).</para>
/// </summary>
internal static class SparkRegex
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
    /// Validates an RE2 regex pattern and returns it as-is for Spark.
    /// Spark's java.util.regex engine handles inline <c>(?i)</c> natively, so
    /// the returned <see cref="RegexResult.CaseInsensitive"/> is always <c>false</c>
    /// — the engine will honour the inline flag if present.
    /// </summary>
    /// <exception cref="ConversionException">If the pattern is invalid or contains unsupported features.</exception>
    internal static RegexResult ConvertRe2ToSpark(string re2Pattern)
    {
        if (re2Pattern.Length > MaxPatternLength)
        {
            throw new ConversionException(
                "Invalid pattern in expression",
                string.Format(CultureInfo.InvariantCulture,
                    "pattern length {0} exceeds limit of {1} characters",
                    re2Pattern.Length, MaxPatternLength));
        }
        try
        {
            _ = new Regex(re2Pattern);
        }
        catch (ArgumentException e)
        {
            throw new ConversionException(
                "Invalid pattern in expression",
                "regex pattern does not compile: " + e.Message);
        }
        if (re2Pattern.Contains("(?=", StringComparison.Ordinal) || re2Pattern.Contains("(?!", StringComparison.Ordinal))
        {
            throw new ConversionException(
                "Invalid pattern in expression",
                "lookahead assertions (?=...), (?!...) are not supported in Spark regex");
        }
        if (re2Pattern.Contains("(?<=", StringComparison.Ordinal) || re2Pattern.Contains("(?<!", StringComparison.Ordinal))
        {
            throw new ConversionException(
                "Invalid pattern in expression",
                "lookbehind assertions (?<=...), (?<!...) are not supported in Spark regex");
        }
        if (re2Pattern.Contains("(?P<", StringComparison.Ordinal))
        {
            throw new ConversionException(
                "Invalid pattern in expression",
                "named capture groups (?P<name>...) are not supported in Spark regex");
        }
        if (NestedQuantifiers.IsMatch(re2Pattern))
        {
            throw new ConversionException(
                "Invalid pattern in expression",
                "regex contains catastrophic nested quantifiers that could cause ReDoS");
        }
        ValidateNoNestedQuantifiers(re2Pattern);

        int groupCount = CountUnescapedParens(re2Pattern);
        if (groupCount > MaxGroups)
        {
            throw new ConversionException(
                "Invalid pattern in expression",
                string.Format(CultureInfo.InvariantCulture,
                    "regex contains {0} capture groups, exceeds limit of {1}",
                    groupCount, MaxGroups));
        }
        if (QuantifiedAlternation.IsMatch(re2Pattern))
        {
            throw new ConversionException(
                "Invalid pattern in expression",
                "regex contains quantified alternation that could cause ReDoS");
        }
        int maxDepth = ComputeMaxNestingDepth(re2Pattern);
        if (maxDepth > MaxNestingDepth)
        {
            throw new ConversionException(
                "Invalid pattern in expression",
                string.Format(CultureInfo.InvariantCulture,
                    "nesting depth {0} exceeds limit of {1}", maxDepth, MaxNestingDepth));
        }
        if (re2Pattern.Contains("(?m", StringComparison.Ordinal) || re2Pattern.Contains("(?s", StringComparison.Ordinal) || re2Pattern.Contains("(?-", StringComparison.Ordinal))
        {
            throw new ConversionException(
                "Invalid pattern in expression",
                "inline flags other than (?i) are not supported in Spark regex");
        }
        return new RegexResult(re2Pattern, false);
    }

    private static void ValidateNoNestedQuantifiers(string pattern)
    {
        int depth = 0;
        bool[] groupHasQuantifier = new bool[pattern.Length + 1];
        int stackTop = -1;
        for (int i = 0; i < pattern.Length; i++)
        {
            char ch = pattern[i];
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
                        if (stackTop > 0 && groupHasQuantifier[stackTop])
                        {
                            groupHasQuantifier[stackTop - 1] = true;
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
                case '{':
                    if (stackTop >= 0)
                    {
                        groupHasQuantifier[stackTop] = true;
                    }
                    break;
            }
        }
    }

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
