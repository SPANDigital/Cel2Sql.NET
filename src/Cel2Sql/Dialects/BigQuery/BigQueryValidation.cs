using System.Globalization;
using Cel2Sql.Errors;

namespace Cel2Sql.Dialects.BigQuery;

/// <summary>
/// BigQuery-specific field name validation and reserved keyword management.
/// Ported from the Go <c>dialect/bigquery/validation.go</c> implementation.
/// </summary>
internal static class BigQueryValidation
{
    /// <summary>Maximum identifier length in BigQuery.</summary>
    internal const int MaxIdentifierLength = 300;

    /// <summary>
    /// Set of BigQuery reserved SQL keywords (lowercased).
    /// These cannot be used as unquoted identifiers.
    /// </summary>
    private static readonly IReadOnlySet<string> ReservedSqlKeywords = new HashSet<string>
    {
        "all",
        "and",
        "any",
        "array",
        "as",
        "asc",
        "assert_rows_modified",
        "at",
        "between",
        "by",
        "case",
        "cast",
        "collate",
        "contains",
        "create",
        "cross",
        "cube",
        "current",
        "default",
        "define",
        "desc",
        "distinct",
        "else",
        "end",
        "enum",
        "escape",
        "except",
        "exclude",
        "exists",
        "extract",
        "false",
        "fetch",
        "following",
        "for",
        "from",
        "full",
        "group",
        "grouping",
        "groups",
        "hash",
        "having",
        "if",
        "ignore",
        "in",
        "inner",
        "intersect",
        "interval",
        "into",
        "is",
        "join",
        "lateral",
        "left",
        "like",
        "limit",
        "lookup",
        "merge",
        "natural",
        "new",
        "no",
        "not",
        "null",
        "nulls",
        "of",
        "on",
        "or",
        "order",
        "outer",
        "over",
        "partition",
        "preceding",
        "proto",
        "range",
        "recursive",
        "respect",
        "right",
        "rollup",
        "rows",
        "select",
        "set",
        "some",
        "struct",
        "tablesample",
        "then",
        "to",
        "treat",
        "true",
        "unbounded",
        "union",
        "unnest",
        "using",
        "when",
        "where",
        "window",
        "with",
        "within",
    };

    /// <summary>
    /// Validates a field name for use as a BigQuery identifier.
    /// </summary>
    /// <param name="name">The field name to validate.</param>
    /// <exception cref="ConversionException">
    /// If the field name is empty, too long, contains invalid characters, or is a reserved keyword.
    /// </exception>
    internal static void ValidateFieldName(string? name)
        => FieldNameValidator.Validate(name, "BigQuery", MaxIdentifierLength, ReservedSqlKeywords);

    /// <summary>
    /// Returns the set of reserved SQL keywords for BigQuery.
    /// </summary>
    /// <returns>An unmodifiable set of lowercased reserved keywords.</returns>
    internal static IReadOnlySet<string> GetReservedKeywords() => ReservedSqlKeywords;
}
