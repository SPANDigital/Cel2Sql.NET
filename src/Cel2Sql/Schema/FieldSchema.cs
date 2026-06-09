using System.Collections.ObjectModel;

namespace Cel2Sql.Schema;

/// <summary>
/// Represents a database field type with name, type, and optional nested schema.
/// This type is dialect-agnostic and used by all SQL dialect implementations.
/// </summary>
public sealed record FieldSchema
{
    /// <summary>The field name.</summary>
    public string Name { get; }

    /// <summary>The SQL type name (text, integer, boolean, etc.).</summary>
    public string Type { get; }

    /// <summary>True for array fields.</summary>
    public bool Repeated { get; }

    /// <summary>Number of array dimensions (1 for integer[], 2 for integer[][], etc.).</summary>
    public int Dimensions { get; }

    /// <summary>Nested field schemas for composite types.</summary>
    public IReadOnlyList<FieldSchema> Schema { get; }

    /// <summary>True for json/jsonb types.</summary>
    public bool IsJson { get; }

    /// <summary>True for jsonb (vs json).</summary>
    public bool IsJsonb { get; }

    /// <summary>For arrays: element type name.</summary>
    public string ElementType { get; }

    public FieldSchema(
        string name,
        string type,
        bool repeated = false,
        int dimensions = 0,
        IReadOnlyList<FieldSchema>? schema = null,
        bool isJson = false,
        bool isJsonb = false,
        string? elementType = null)
    {
        Name = name;
        Type = type;
        Repeated = repeated;
        Dimensions = dimensions;
        Schema = schema != null
            ? new ReadOnlyCollection<FieldSchema>(new List<FieldSchema>(schema))
            : Array.Empty<FieldSchema>();
        IsJson = isJson;
        IsJsonb = isJsonb;
        ElementType = elementType ?? "";
    }
}
