using System.Globalization;
using System.Text.RegularExpressions;
using Cel2Sql.Errors;

namespace Cel2Sql.Dialects.Sqlite;

/// <summary>
/// SQLite-specific field name validation and reserved keyword management.
/// Ported from the Go <c>dialect/sqlite/validation.go</c> implementation.
/// </summary>
internal static class SqliteValidation
{
    /// <summary>Pattern for valid SQLite identifiers: starts with letter or underscore, then alphanumeric or underscore.</summary>
    private static readonly Regex FieldNamePattern = new("^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    /// <summary>
    /// Set of SQLite reserved SQL keywords (lowercased).
    /// These cannot be used as unquoted identifiers.
    /// </summary>
    private static readonly IReadOnlySet<string> ReservedSqlKeywords = new HashSet<string>
    {
        "abort",
        "action",
        "add",
        "after",
        "all",
        "alter",
        "always",
        "analyze",
        "and",
        "as",
        "asc",
        "attach",
        "autoincrement",
        "before",
        "begin",
        "between",
        "by",
        "cascade",
        "case",
        "cast",
        "check",
        "collate",
        "column",
        "commit",
        "conflict",
        "constraint",
        "create",
        "cross",
        "current",
        "current_date",
        "current_time",
        "current_timestamp",
        "database",
        "default",
        "deferrable",
        "deferred",
        "delete",
        "desc",
        "detach",
        "distinct",
        "do",
        "drop",
        "each",
        "else",
        "end",
        "escape",
        "except",
        "exclude",
        "exclusive",
        "exists",
        "explain",
        "fail",
        "filter",
        "first",
        "following",
        "for",
        "foreign",
        "from",
        "full",
        "glob",
        "group",
        "groups",
        "having",
        "if",
        "ignore",
        "immediate",
        "in",
        "index",
        "indexed",
        "initially",
        "inner",
        "insert",
        "instead",
        "intersect",
        "into",
        "is",
        "isnull",
        "join",
        "key",
        "last",
        "left",
        "like",
        "limit",
        "match",
        "materialized",
        "natural",
        "no",
        "not",
        "nothing",
        "notnull",
        "null",
        "nulls",
        "of",
        "offset",
        "on",
        "or",
        "order",
        "others",
        "outer",
        "over",
        "partition",
        "plan",
        "pragma",
        "preceding",
        "primary",
        "query",
        "raise",
        "range",
        "recursive",
        "references",
        "regexp",
        "reindex",
        "release",
        "rename",
        "replace",
        "restrict",
        "returning",
        "right",
        "rollback",
        "row",
        "rows",
        "savepoint",
        "select",
        "set",
        "table",
        "temp",
        "temporary",
        "then",
        "ties",
        "to",
        "transaction",
        "trigger",
        "unbounded",
        "union",
        "unique",
        "update",
        "using",
        "vacuum",
        "values",
        "view",
        "virtual",
        "when",
        "where",
        "window",
        "with",
        "without",
    };

    /// <summary>
    /// Validates a field name for use as a SQLite identifier.
    /// </summary>
    /// <param name="name">The field name to validate.</param>
    /// <exception cref="ConversionException">
    /// If the field name is empty, contains invalid characters, or is a reserved keyword.
    /// </exception>
    internal static void ValidateFieldName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ConversionException("field name cannot be empty",
                "field name cannot be empty");
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
    /// Returns the set of reserved SQL keywords for SQLite.
    /// </summary>
    /// <returns>An unmodifiable set of lowercased reserved keywords.</returns>
    internal static IReadOnlySet<string> GetReservedKeywords() => ReservedSqlKeywords;
}
