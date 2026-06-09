using ProtoType = Google.Api.Expr.V1Alpha1.Type;

namespace Cel2Sql.Cel;

/// <summary>
/// A lightweight wrapper over a checked CEL type. Collapses the proto type representation
/// into the small surface the converter needs (kind + list element type).
/// Equivalent to the subset of dev.cel's <c>CelType</c>/<c>ListType</c>/<c>MapType</c> usage.
/// </summary>
public sealed class CelTypeRef
{
    /// <summary>The classified kind of this type.</summary>
    public CelTypeKind Kind { get; }

    /// <summary>For list types, the element type (null otherwise).</summary>
    public CelTypeRef? ElemType { get; }

    private CelTypeRef(CelTypeKind kind, CelTypeRef? elemType)
    {
        Kind = kind;
        ElemType = elemType;
    }

    /// <summary>True when this is a list type carrying an element type.</summary>
    public bool HasElemType => Kind == CelTypeKind.List && ElemType != null;

    internal static CelTypeRef From(ProtoType t)
    {
        switch (t.TypeKindCase)
        {
            case ProtoType.TypeKindOneofCase.Primitive:
            case ProtoType.TypeKindOneofCase.Wrapper:
                var prim = t.TypeKindCase == ProtoType.TypeKindOneofCase.Primitive ? t.Primitive : t.Wrapper;
                return new CelTypeRef(MapPrimitive(prim), null);
            case ProtoType.TypeKindOneofCase.WellKnown:
                return new CelTypeRef(MapWellKnown(t.WellKnown), null);
            case ProtoType.TypeKindOneofCase.ListType:
                var elem = t.ListType.ElemType != null ? From(t.ListType.ElemType) : null;
                return new CelTypeRef(CelTypeKind.List, elem);
            case ProtoType.TypeKindOneofCase.MapType:
                return new CelTypeRef(CelTypeKind.Map, null);
            case ProtoType.TypeKindOneofCase.Null:
                return new CelTypeRef(CelTypeKind.Null, null);
            case ProtoType.TypeKindOneofCase.Dyn:
                return new CelTypeRef(CelTypeKind.Dyn, null);
            default:
                return new CelTypeRef(CelTypeKind.Unknown, null);
        }
    }

    private static CelTypeKind MapPrimitive(ProtoType.Types.PrimitiveType p) => p switch
    {
        ProtoType.Types.PrimitiveType.Bool => CelTypeKind.Bool,
        ProtoType.Types.PrimitiveType.Int64 => CelTypeKind.Int,
        ProtoType.Types.PrimitiveType.Uint64 => CelTypeKind.Uint,
        ProtoType.Types.PrimitiveType.Double => CelTypeKind.Double,
        ProtoType.Types.PrimitiveType.String => CelTypeKind.String,
        ProtoType.Types.PrimitiveType.Bytes => CelTypeKind.Bytes,
        _ => CelTypeKind.Unknown,
    };

    private static CelTypeKind MapWellKnown(ProtoType.Types.WellKnownType w) => w switch
    {
        ProtoType.Types.WellKnownType.Timestamp => CelTypeKind.Timestamp,
        ProtoType.Types.WellKnownType.Duration => CelTypeKind.Duration,
        ProtoType.Types.WellKnownType.Any => CelTypeKind.Dyn,
        _ => CelTypeKind.Unknown,
    };
}
