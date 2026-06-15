using System.Globalization;
using System.Text.RegularExpressions;
using Cel2Sql.Errors;

namespace Cel2Sql.Dialects;

/// <summary>
/// Shared field-name validation used by the per-dialect validation classes.
/// The identifier rule — non-empty, optional max-length, must match
/// <c>^[a-zA-Z_][a-zA-Z0-9_]*$</c>, and not be a reserved keyword — is common
/// to PostgreSQL, BigQuery, MySQL, DuckDB and SQLite; only the engine display
/// name, the max-identifier length, and the reserved-keyword set differ, so
/// those are passed in. (Spark uses different messages/quoting and keeps its
/// own implementation.)
/// </summary>
internal static class FieldNameValidator
{
    /// <summary>Valid identifier: starts with a letter or underscore, then alphanumeric or underscore.</summary>
    private static readonly Regex FieldNamePattern = new("^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    /// <summary>
    /// Validates a field name against the common identifier rules.
    /// </summary>
    /// <param name="name">The field name to validate.</param>
    /// <param name="engineDisplayName">Engine name used in the length-limit message (e.g. "PostgreSQL").</param>
    /// <param name="maxIdentifierLength">Maximum identifier length; <c>0</c> means no limit (length check skipped).</param>
    /// <param name="reservedKeywords">The dialect's lowercased reserved-keyword set.</param>
    /// <exception cref="ConversionException">If the name is empty, too long, malformed, or a reserved keyword.</exception>
    internal static void Validate(string? name, string engineDisplayName, int maxIdentifierLength, IReadOnlySet<string> reservedKeywords)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw ConversionException.Of("field name cannot be empty", "field name cannot be empty");
        }
        if (maxIdentifierLength > 0 && name.Length > maxIdentifierLength)
        {
            string detail = string.Format(CultureInfo.InvariantCulture,
                "field name \"{0}\" exceeds {1} maximum identifier length of {2} characters",
                name, engineDisplayName, maxIdentifierLength);
            throw ConversionException.Of("Invalid field name", detail);
        }
        if (!FieldNamePattern.IsMatch(name))
        {
            string detail = string.Format(CultureInfo.InvariantCulture,
                "field name \"{0}\" must start with a letter or underscore and contain only alphanumeric characters and underscores",
                name);
            throw ConversionException.Of("Invalid field name", detail);
        }
        if (reservedKeywords.Contains(name.ToLowerInvariant()))
        {
            string detail = string.Format(CultureInfo.InvariantCulture,
                "field name \"{0}\" is a reserved SQL keyword and cannot be used without quoting",
                name);
            throw ConversionException.Of("Invalid field name", detail);
        }
    }
}
