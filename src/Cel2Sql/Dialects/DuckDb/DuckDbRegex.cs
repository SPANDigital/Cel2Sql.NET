using Cel2Sql.Errors;

namespace Cel2Sql.Dialects.DuckDb;

/// <summary>
/// Converts RE2 regex patterns to DuckDB's native regex format.
/// DuckDB uses RE2 natively, so minimal conversion is needed.
/// Performs security validation to prevent ReDoS attacks (CWE-1333) via the
/// shared <see cref="RegexGuard"/> primitives.
///
/// <para>Ported from the Go <c>dialect/duckdb/regex.go</c> implementation.</para>
/// </summary>
internal static class DuckDbRegex
{
    private const string UserMessage = "Invalid pattern in expression";
    private const string EngineLabel = "DuckDB regex";

    /// <summary>
    /// Converts an RE2 regex pattern to DuckDB's native regex format.
    /// Since DuckDB uses RE2 natively, this primarily performs security validation
    /// and handles the <c>(?i)</c> flag extraction.
    ///
    /// <para>Security checks performed (in order):
    /// <list type="number">
    ///   <item>Pattern length limit</item>
    ///   <item>Validate pattern compiles</item>
    ///   <item>Reject lookahead, lookbehind, named groups</item>
    ///   <item>Detect catastrophic nested quantifiers</item>
    ///   <item>Count and limit capture groups</item>
    ///   <item>Detect exponential alternation patterns</item>
    ///   <item>Check nesting depth</item>
    ///   <item>Handle <c>(?i)</c> flag</item>
    ///   <item>Reject other inline flags</item>
    ///   <item>Convert non-capturing groups to plain groups</item>
    /// </list></para>
    /// </summary>
    /// <param name="re2Pattern">The RE2 regex pattern.</param>
    /// <returns>A <see cref="RegexResult"/> with the DuckDB-compatible pattern and case sensitivity flag.</returns>
    /// <exception cref="ConversionException">If the pattern is invalid or contains unsupported features.</exception>
    internal static RegexResult ConvertRe2ToDuckDb(string re2Pattern)
    {
        // 1. Check pattern length
        RegexGuard.CheckLength(re2Pattern, UserMessage);

        // 2. Validate pattern compiles
        RegexGuard.CheckCompiles(re2Pattern, UserMessage);

        // 3. Reject unsupported features: lookahead, lookbehind, named groups
        RegexGuard.CheckUnsupportedConstructs(re2Pattern, UserMessage, EngineLabel);

        // 4. Detect catastrophic nested quantifiers
        RegexGuard.CheckCatastrophicQuantifiers(re2Pattern, UserMessage);

        // 5. Count and limit capture groups
        RegexGuard.CheckGroupCount(re2Pattern, UserMessage);

        // 6. Detect exponential alternation patterns
        RegexGuard.CheckQuantifiedAlternation(re2Pattern, UserMessage);

        // 7. Check nesting depth
        RegexGuard.CheckNestingDepth(re2Pattern, UserMessage);

        // 8. Handle (?i) flag
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

        return new RegexResult(pattern, caseInsensitive);
    }
}
