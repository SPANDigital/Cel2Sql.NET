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
/// The validation logic is shared with the other RE2-style dialects via
/// <see cref="RegexGuard"/> and prevents ReDoS attacks (CWE-1333).</para>
/// </summary>
internal static class SparkRegex
{
    private const string UserMessage = "Invalid pattern in expression";
    private const string EngineLabel = "Spark regex";

    /// <summary>
    /// Validates an RE2 regex pattern and returns it as-is for Spark.
    /// Spark's java.util.regex engine handles inline <c>(?i)</c> natively, so
    /// the returned <see cref="RegexResult.CaseInsensitive"/> is always <c>false</c>
    /// — the engine will honour the inline flag if present.
    /// </summary>
    /// <exception cref="ConversionException">If the pattern is invalid or contains unsupported features.</exception>
    internal static RegexResult ConvertRe2ToSpark(string re2Pattern)
    {
        RegexGuard.CheckLength(re2Pattern, UserMessage);
        RegexGuard.CheckCompiles(re2Pattern, UserMessage);
        RegexGuard.CheckUnsupportedConstructs(re2Pattern, UserMessage, EngineLabel);
        RegexGuard.CheckCatastrophicQuantifiers(re2Pattern, UserMessage);
        RegexGuard.CheckGroupCount(re2Pattern, UserMessage);
        RegexGuard.CheckQuantifiedAlternation(re2Pattern, UserMessage);
        RegexGuard.CheckNestingDepth(re2Pattern, UserMessage);
        RegexGuard.CheckInlineFlags(re2Pattern, UserMessage, EngineLabel);

        // Spark's java.util.regex engine handles inline (?i) natively, so the
        // pattern passes through unchanged with caseInsensitive=false.
        return new RegexResult(re2Pattern, false);
    }
}
