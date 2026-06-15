using Cel2Sql.Errors;

namespace Cel2Sql.Dialects.MySql;

/// <summary>
/// Converts RE2 regex patterns to MySQL/ICU regex format.
/// Performs security validation to prevent ReDoS attacks (CWE-1333) via the
/// shared <see cref="RegexGuard"/> primitives.
///
/// <para>Ported from the Go <c>dialect/mysql/regex.go</c> implementation.</para>
/// </summary>
internal static class MySqlRegex
{
    private const string UserMessage = "Invalid regex pattern";
    private const string EngineLabel = "MySQL regex";

    /// <summary>
    /// Converts an RE2 regex pattern to MySQL/ICU regex format.
    ///
    /// <para>Security checks performed (in order):
    /// <list type="number">
    ///   <item>Pattern length limit</item>
    ///   <item>Validate pattern compiles as a regex</item>
    ///   <item>Reject lookahead, lookbehind, and named groups</item>
    ///   <item>Detect catastrophic nested quantifiers</item>
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
        RegexGuard.CheckLength(re2Pattern, UserMessage);

        // 2. Validate pattern compiles
        RegexGuard.CheckCompiles(re2Pattern, UserMessage);

        // 3. Reject lookahead, lookbehind, and named groups
        RegexGuard.CheckUnsupportedConstructs(re2Pattern, UserMessage, EngineLabel);

        // 4. Detect catastrophic nested quantifiers
        RegexGuard.CheckCatastrophicQuantifiers(re2Pattern, UserMessage);

        // 5. Count and limit capture groups
        RegexGuard.CheckGroupCount(re2Pattern, UserMessage);

        // 6. Detect exponential alternation patterns
        RegexGuard.CheckQuantifiedAlternation(re2Pattern, UserMessage);

        // 7. Check nesting depth
        RegexGuard.CheckNestingDepth(re2Pattern, UserMessage);

        // 8. Handle (?i) flag -> set caseInsensitive=true, strip prefix
        bool caseInsensitive = false;
        string pattern = re2Pattern;
        if (pattern.StartsWith("(?i)", StringComparison.Ordinal))
        {
            caseInsensitive = true;
            pattern = pattern.Substring(4);
        }

        // 9. Reject other inline flags
        RegexGuard.CheckInlineFlags(pattern, UserMessage, EngineLabel);

        // 10. Convert non-capturing groups (?:...) to plain groups (...)
        pattern = pattern.Replace("(?:", "(");

        // MySQL ICU supports \d, \w, \s, \b natively - no conversion needed

        return new RegexResult(pattern, caseInsensitive);
    }
}
