using Cel.Checker;
using Google.Protobuf.WellKnownTypes;
using ProtoType = Google.Api.Expr.V1Alpha1.Type;

namespace Cel2Sql.Cel;

/// <summary>
/// A CEL variable type usable in declarations. Factory methods mirror the common CEL types.
/// Insulates callers from the underlying Cel.NET proto type representation.
/// </summary>
public sealed class CelVarType
{
    internal ProtoType Proto { get; }
    private CelVarType(ProtoType proto) => Proto = proto;

    public static CelVarType String { get; } = Prim(ProtoType.Types.PrimitiveType.String);
    public static CelVarType Int { get; } = Prim(ProtoType.Types.PrimitiveType.Int64);
    public static CelVarType Uint { get; } = Prim(ProtoType.Types.PrimitiveType.Uint64);
    public static CelVarType Bool { get; } = Prim(ProtoType.Types.PrimitiveType.Bool);
    public static CelVarType Double { get; } = Prim(ProtoType.Types.PrimitiveType.Double);
    public static CelVarType Bytes { get; } = Prim(ProtoType.Types.PrimitiveType.Bytes);
    public static CelVarType Timestamp { get; } = new(Decls.NewWellKnownType(ProtoType.Types.WellKnownType.Timestamp));
    public static CelVarType Duration { get; } = new(Decls.NewWellKnownType(ProtoType.Types.WellKnownType.Duration));
    public static CelVarType Dyn { get; } = new(new ProtoType { Dyn = new Empty() });

    public static CelVarType List(CelVarType elem) => new(Decls.NewListType(elem.Proto));
    public static CelVarType Map(CelVarType key, CelVarType value) => new(Decls.NewMapType(key.Proto, value.Proto));

    private static CelVarType Prim(ProtoType.Types.PrimitiveType p) => new(Decls.NewPrimitiveType(p));
}
