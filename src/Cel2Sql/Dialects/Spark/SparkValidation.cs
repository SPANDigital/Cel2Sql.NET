using System.Globalization;
using System.Text.RegularExpressions;
using Cel2Sql.Errors;

namespace Cel2Sql.Dialects.Spark;

/// <summary>
/// Spark-specific field name validation and reserved keyword management.
/// Ported from the Go <c>dialect/spark/validation.go</c> implementation.
/// </summary>
internal static class SparkValidation
{
    /// <summary>Spark / Hive identifier limit.</summary>
    internal const int MaxIdentifierLength = 128;

    /// <summary>Pattern for valid Spark identifiers (unquoted form): letter or underscore start, alphanumeric or underscore body.</summary>
    private static readonly Regex FieldNamePattern = new("^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    /// <summary>
    /// Spark SQL reserved keywords (lowercased). Sourced from the Apache Spark docs
    /// (sql-ref-ansi-compliance.html#sql-keywords) plus the standard SQL set.
    /// </summary>
    private static readonly IReadOnlySet<string> ReservedSqlKeywords = new HashSet<string>
    {
        "all", "alter", "and", "anti", "any", "array", "as", "asc", "between", "both",
        "by", "case", "cast", "check", "cluster", "collate", "column", "create", "cross",
        "cube", "current", "current_date", "current_time", "current_timestamp",
        "current_user", "default", "delete", "desc", "describe", "distinct", "drop",
        "else", "end", "escape", "except", "exists", "false", "fetch", "filter", "for",
        "foreign", "from", "full", "function", "grant", "group", "grouping", "having",
        "hour", "in", "inner", "insert", "intersect", "interval", "into", "is", "join",
        "lateral", "leading", "left", "like", "limit", "local", "map", "minute", "month",
        "natural", "no", "not", "null", "of", "on", "only", "or", "order", "outer",
        "overlaps", "primary", "references", "right", "rollup", "row", "rows", "second",
        "select", "semi", "session_user", "set", "some", "struct", "table", "tablesample",
        "then", "time", "to", "trailing", "true", "union", "unique", "unknown", "update",
        "user", "using", "values", "when", "where", "window", "with", "year",
    };

    internal static void ValidateFieldName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ConversionException(
                "Invalid field name",
                "field name cannot be empty");
        }
        if (name.Length > MaxIdentifierLength)
        {
            throw new ConversionException(
                "Invalid field name",
                string.Format(CultureInfo.InvariantCulture,
                    "field name length {0} exceeds Spark limit of {1}",
                    name.Length, MaxIdentifierLength));
        }
        if (!FieldNamePattern.IsMatch(name))
        {
            throw new ConversionException(
                "Invalid field name",
                string.Format(CultureInfo.InvariantCulture,
                    "field name '{0}' must start with a letter or underscore "
                    + "and contain only alphanumeric characters and underscores", name));
        }
        if (ReservedSqlKeywords.Contains(name.ToLowerInvariant()))
        {
            throw new ConversionException(
                "Invalid field name",
                string.Format(CultureInfo.InvariantCulture,
                    "field name '{0}' is a reserved SQL keyword and cannot be used "
                    + "without quoting", name));
        }
    }

    internal static IReadOnlySet<string> GetReservedKeywords() => ReservedSqlKeywords;
}
