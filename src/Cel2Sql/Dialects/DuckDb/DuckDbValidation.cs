using System.Globalization;
using Cel2Sql.Errors;

namespace Cel2Sql.Dialects.DuckDb;

/// <summary>
/// DuckDB-specific field name validation and reserved keyword management.
/// Ported from the Go <c>dialect/duckdb/validation.go</c> implementation.
/// </summary>
internal static class DuckDbValidation
{
    /// <summary>DuckDB has no maximum identifier length limit.</summary>
    internal const int MaxIdentifierLength = 0;

    /// <summary>
    /// Set of DuckDB reserved SQL keywords (lowercased).
    /// These cannot be used as unquoted identifiers.
    /// </summary>
    private static readonly IReadOnlySet<string> ReservedSqlKeywords = new HashSet<string>
    {
        "all",
        "alter",
        "analyse",
        "analyze",
        "and",
        "any",
        "array",
        "as",
        "asc",
        "asymmetric",
        "between",
        "both",
        "case",
        "cast",
        "check",
        "collate",
        "column",
        "constraint",
        "create",
        "cross",
        "current_catalog",
        "current_date",
        "current_role",
        "current_schema",
        "current_time",
        "current_timestamp",
        "current_user",
        "default",
        "deferrable",
        "desc",
        "distinct",
        "do",
        "else",
        "end",
        "except",
        "exists",
        "false",
        "fetch",
        "for",
        "foreign",
        "from",
        "full",
        "grant",
        "group",
        "having",
        "in",
        "initially",
        "inner",
        "intersect",
        "into",
        "is",
        "isnull",
        "join",
        "lateral",
        "leading",
        "left",
        "like",
        "limit",
        "localtime",
        "localtimestamp",
        "natural",
        "not",
        "notnull",
        "null",
        "offset",
        "on",
        "only",
        "or",
        "order",
        "outer",
        "overlaps",
        "placing",
        "primary",
        "references",
        "returning",
        "right",
        "select",
        "session_user",
        "similar",
        "some",
        "symmetric",
        "table",
        "then",
        "to",
        "trailing",
        "true",
        "union",
        "unique",
        "using",
        "variadic",
        "when",
        "where",
        "window",
        "with",
    };

    /// <summary>
    /// Validates a field name for use as a DuckDB identifier.
    /// </summary>
    /// <param name="name">The field name to validate.</param>
    /// <exception cref="ConversionException">
    /// If the field name is empty, contains invalid characters, or is a reserved keyword.
    /// </exception>
    internal static void ValidateFieldName(string? name)
        => FieldNameValidator.Validate(name, "DuckDB", MaxIdentifierLength, ReservedSqlKeywords);

    /// <summary>
    /// Returns the set of reserved SQL keywords for DuckDB.
    /// </summary>
    /// <returns>An unmodifiable set of lowercased reserved keywords.</returns>
    internal static IReadOnlySet<string> GetReservedKeywords() => ReservedSqlKeywords;
}
