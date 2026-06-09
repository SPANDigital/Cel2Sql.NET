namespace Cel2Sql;

/// <summary>
/// Represents the output of a CEL to SQL conversion with parameterized queries.
/// Contains the SQL string with placeholders ($1, $2, etc.) and the corresponding parameter values.
/// </summary>
/// <param name="Sql">The generated SQL WHERE clause with placeholders.</param>
/// <param name="Parameters">Parameter values in order ($1, $2, etc.).</param>
public sealed record ConvertResult(string Sql, IReadOnlyList<object?> Parameters);
