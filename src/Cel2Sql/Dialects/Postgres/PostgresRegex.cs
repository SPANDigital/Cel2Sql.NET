using Cel2Sql.Errors;

namespace Cel2Sql.Dialects.Postgres;

/// <summary>
/// Converts RE2 regex patterns to POSIX ERE format for PostgreSQL.
/// Performs security validation to prevent ReDoS attacks (CWE-1333) via the
/// shared <see cref="RegexGuard"/> primitives.
///
/// <para>Ported from the Go <c>dialect/postgres/regex.go</c> implementation.</para>
/// </summary>
internal static class PostgresRegex
{
    private const string UserMessage = "Invalid pattern in expression";
    private const string EngineLabel = "PostgreSQL POSIX regex";

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
        RegexGuard.CheckLength(re2Pattern, UserMessage);

        // 2. Extract case-insensitive flag
        bool caseInsensitive = false;
        string pattern = re2Pattern;
        if (pattern.StartsWith("(?i)", StringComparison.Ordinal))
        {
            caseInsensitive = true;
            pattern = pattern.Substring(4);
        }

        // 3. Detect unsupported features (incl. inline flags other than (?i))
        RegexGuard.CheckUnsupportedConstructs(pattern, UserMessage, EngineLabel);
        RegexGuard.CheckInlineFlags(pattern, UserMessage, EngineLabel);

        // 4. Detect catastrophic nested quantifiers
        RegexGuard.CheckCatastrophicQuantifiers(pattern, UserMessage);

        // 5. Count and limit capture groups
        RegexGuard.CheckGroupCount(pattern, UserMessage);

        // 6. Detect exponential alternation patterns
        RegexGuard.CheckQuantifiedAlternation(pattern, UserMessage);

        // 7. Check nesting depth
        RegexGuard.CheckNestingDepth(pattern, UserMessage);

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
}
