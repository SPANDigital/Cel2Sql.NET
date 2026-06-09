using System.Globalization;
using System.Text.RegularExpressions;
using Cel2Sql.Errors;

namespace Cel2Sql.Dialects.Postgres;

/// <summary>
/// PostgreSQL-specific field name validation and reserved keyword management.
/// Ported from the Go <c>dialect/postgres/validation.go</c> implementation.
/// </summary>
internal static class PostgresValidation
{
    /// <summary>Maximum identifier length in PostgreSQL (NAMEDATALEN - 1).</summary>
    internal const int MaxIdentifierLength = 63;

    /// <summary>Pattern for valid PostgreSQL identifiers: starts with letter or underscore, then alphanumeric or underscore.</summary>
    private static readonly Regex FieldNamePattern = new("^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    /// <summary>
    /// Set of PostgreSQL reserved SQL keywords (lowercased).
    /// These cannot be used as unquoted identifiers.
    /// </summary>
    private static readonly IReadOnlySet<string> ReservedSqlKeywords = new HashSet<string>
    {
        "all",
        "analyse",
        "analyze",
        "and",
        "any",
        "array",
        "as",
        "asc",
        "asymmetric",
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
        "false",
        "fetch",
        "for",
        "foreign",
        "from",
        "grant",
        "group",
        "having",
        "in",
        "initially",
        "inner",
        "intersect",
        "into",
        "is",
        "join",
        "leading",
        "left",
        "like",
        "limit",
        "localtime",
        "localtimestamp",
        "natural",
        "not",
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
        "user",
        "using",
        "variadic",
        "when",
        "where",
        "window",
        "with",
        "alter",
        "delete",
        "drop",
        "insert",
        "update",
    };

    /// <summary>
    /// Validates a field name for use as a PostgreSQL identifier.
    /// </summary>
    /// <param name="name">The field name to validate.</param>
    /// <exception cref="ConversionException">
    /// If the field name is empty, too long, contains invalid characters, or is a reserved keyword.
    /// </exception>
    internal static void ValidateFieldName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ConversionException("field name cannot be empty",
                "field name cannot be empty");
        }
        if (name.Length > MaxIdentifierLength)
        {
            string detail = string.Format(CultureInfo.InvariantCulture,
                "field name \"{0}\" exceeds PostgreSQL maximum identifier length of {1} characters",
                name, MaxIdentifierLength);
            throw new ConversionException("Invalid field name", detail);
        }
        if (!FieldNamePattern.IsMatch(name))
        {
            string detail = string.Format(CultureInfo.InvariantCulture,
                "field name \"{0}\" must start with a letter or underscore and contain only alphanumeric characters and underscores",
                name);
            throw new ConversionException("Invalid field name", detail);
        }
        if (ReservedSqlKeywords.Contains(name.ToLowerInvariant()))
        {
            string detail = string.Format(CultureInfo.InvariantCulture,
                "field name \"{0}\" is a reserved SQL keyword and cannot be used without quoting",
                name);
            throw new ConversionException("Invalid field name", detail);
        }
    }

    /// <summary>
    /// Returns the set of reserved SQL keywords for PostgreSQL.
    /// </summary>
    /// <returns>An unmodifiable set of lowercased reserved keywords.</returns>
    internal static IReadOnlySet<string> GetReservedKeywords() => ReservedSqlKeywords;
}
