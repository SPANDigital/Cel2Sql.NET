using System.Collections.ObjectModel;

namespace Cel2Sql.Schema;

/// <summary>
/// Represents a table schema with O(1) field lookup.
/// Contains a list of fields for ordered iteration and a map index for fast lookups.
/// </summary>
public sealed class Schema
{
    private readonly IReadOnlyList<FieldSchema> _fields;
    private readonly Dictionary<string, FieldSchema> _fieldIndex;

    /// <summary>Creates a new Schema with field indexing for O(1) lookups.</summary>
    public Schema(IReadOnlyList<FieldSchema> fields)
    {
        var index = new Dictionary<string, FieldSchema>(fields.Count);
        foreach (var field in fields)
        {
            index[field.Name] = field;
        }
        _fields = new ReadOnlyCollection<FieldSchema>(new List<FieldSchema>(fields));
        _fieldIndex = index;
    }

    /// <summary>Returns the ordered list of field schemas.</summary>
    public IReadOnlyList<FieldSchema> Fields => _fields;

    /// <summary>Performs an O(1) lookup for a field by name. Returns null if not found.</summary>
    public FieldSchema? FindField(string name) =>
        _fieldIndex.TryGetValue(name, out var f) ? f : null;

    /// <summary>Returns the number of fields in the schema.</summary>
    public int Len => _fields.Count;
}
