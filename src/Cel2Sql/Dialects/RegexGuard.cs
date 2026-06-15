using System.Globalization;
using System.Text.RegularExpressions;
using Cel2Sql.Errors;

namespace Cel2Sql.Dialects;

/// <summary>
/// Shared RE2 regex validation primitives used by the dialect-specific regex
/// converters (<c>PostgresRegex</c>, <c>MySqlRegex</c>, <c>DuckDbRegex</c>,
/// <c>BigQueryRegex</c>, <c>SparkRegex</c>). Centralises the ReDoS-protection
/// limits, the compiled detector patterns, and the structural checks that were
/// previously duplicated verbatim across every dialect (CWE-1333).
///
/// <para>The checks are exposed as granular methods rather than a single
/// pipeline so each dialect can compose them in its own order, with its own
/// user-facing message and engine label, preserving the exact behaviour of the
/// Go/Java upstreams it is ported from.</para>
/// </summary>
internal static class RegexGuard
{
    /// <summary>Maximum allowed regex pattern length.</summary>
    internal const int MaxPatternLength = 500;

    /// <summary>Maximum allowed capture groups in a pattern.</summary>
    internal const int MaxGroups = 20;

    /// <summary>Maximum allowed nesting depth of parenthesized groups.</summary>
    internal const int MaxNestingDepth = 10;

    private static readonly Regex NestedQuantifiers = new("[*+][*+]", RegexOptions.Compiled);
    private static readonly Regex QuantifiedAlternation = new(@"\([^)]*\|[^)]*\)[*+]", RegexOptions.Compiled);

    /// <summary>Rejects patterns longer than <see cref="MaxPatternLength"/> characters.</summary>
    internal static void CheckLength(string pattern, string userMessage)
    {
        if (pattern.Length > MaxPatternLength)
        {
            throw ConversionException.Of(
                userMessage,
                string.Format(CultureInfo.InvariantCulture,
                    "pattern length {0} exceeds limit of {1} characters",
                    pattern.Length, MaxPatternLength));
        }
    }

    /// <summary>Rejects patterns that do not compile as a .NET regex.</summary>
    internal static void CheckCompiles(string pattern, string userMessage)
    {
        try
        {
            _ = new Regex(pattern);
        }
        catch (ArgumentException e)
        {
            throw ConversionException.Of(
                userMessage,
                "regex pattern does not compile: " + e.Message);
        }
    }

    /// <summary>
    /// Rejects lookahead, lookbehind, and named capture groups — constructs not
    /// supported by the target engine. <paramref name="engineLabel"/> names the
    /// engine in the detail message (e.g. "DuckDB regex", "PostgreSQL POSIX regex").
    /// </summary>
    internal static void CheckUnsupportedConstructs(string pattern, string userMessage, string engineLabel)
    {
        if (pattern.Contains("(?=", StringComparison.Ordinal) || pattern.Contains("(?!", StringComparison.Ordinal))
        {
            throw ConversionException.Of(
                userMessage,
                "lookahead assertions (?=...), (?!...) are not supported in " + engineLabel);
        }
        if (pattern.Contains("(?<=", StringComparison.Ordinal) || pattern.Contains("(?<!", StringComparison.Ordinal))
        {
            throw ConversionException.Of(
                userMessage,
                "lookbehind assertions (?<=...), (?<!...) are not supported in " + engineLabel);
        }
        if (pattern.Contains("(?P<", StringComparison.Ordinal))
        {
            throw ConversionException.Of(
                userMessage,
                "named capture groups (?P<name>...) are not supported in " + engineLabel);
        }
    }

    /// <summary>
    /// Detects catastrophic nested quantifiers — both the simple adjacent form
    /// (<c>a**</c>) and quantified groups that themselves contain quantifiers
    /// (<c>(a+)+</c>) — which can cause exponential backtracking (ReDoS).
    /// </summary>
    internal static void CheckCatastrophicQuantifiers(string pattern, string userMessage)
    {
        if (NestedQuantifiers.IsMatch(pattern))
        {
            throw ConversionException.Of(
                userMessage,
                "regex contains catastrophic nested quantifiers that could cause ReDoS");
        }
        ValidateNoNestedQuantifiers(pattern, userMessage);
    }

    /// <summary>Rejects patterns with more than <see cref="MaxGroups"/> capture groups.</summary>
    internal static void CheckGroupCount(string pattern, string userMessage)
    {
        int groupCount = CountUnescapedParens(pattern);
        if (groupCount > MaxGroups)
        {
            throw ConversionException.Of(
                userMessage,
                string.Format(CultureInfo.InvariantCulture,
                    "regex contains {0} capture groups, exceeds limit of {1}",
                    groupCount, MaxGroups));
        }
    }

    /// <summary>Detects quantified alternation (<c>(a|b)+</c>) that could cause ReDoS.</summary>
    internal static void CheckQuantifiedAlternation(string pattern, string userMessage)
    {
        if (QuantifiedAlternation.IsMatch(pattern))
        {
            throw ConversionException.Of(
                userMessage,
                "regex contains quantified alternation that could cause ReDoS");
        }
    }

    /// <summary>Rejects patterns nested deeper than <see cref="MaxNestingDepth"/>.</summary>
    internal static void CheckNestingDepth(string pattern, string userMessage)
    {
        int maxDepth = ComputeMaxNestingDepth(pattern);
        if (maxDepth > MaxNestingDepth)
        {
            throw ConversionException.Of(
                userMessage,
                string.Format(CultureInfo.InvariantCulture,
                    "nesting depth {0} exceeds limit of {1}", maxDepth, MaxNestingDepth));
        }
    }

    /// <summary>
    /// Rejects inline flags other than <c>(?i)</c> (i.e. <c>(?m</c>, <c>(?s</c>,
    /// <c>(?-</c>). <paramref name="engineLabel"/> names the engine in the detail message.
    /// </summary>
    internal static void CheckInlineFlags(string pattern, string userMessage, string engineLabel)
    {
        if (pattern.Contains("(?m", StringComparison.Ordinal) || pattern.Contains("(?s", StringComparison.Ordinal) || pattern.Contains("(?-", StringComparison.Ordinal))
        {
            throw ConversionException.Of(
                userMessage,
                "inline flags other than (?i) are not supported in " + engineLabel);
        }
    }

    /// <summary>
    /// Validates that no quantified groups contain inner quantifiers (nested quantifiers).
    /// This detects patterns like <c>(a+)+</c> that can cause catastrophic backtracking.
    /// </summary>
    private static void ValidateNoNestedQuantifiers(string pattern, string userMessage)
    {
        int depth = 0;
        bool[] groupHasQuantifier = new bool[pattern.Length + 1]; // oversized but safe
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
                                    throw ConversionException.Of(
                                        userMessage,
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
