using Google.Api.Expr.V1Alpha1;
using ProtoExpr = Google.Api.Expr.V1Alpha1.Expr;
using ProtoConstant = Google.Api.Expr.V1Alpha1.Constant;
using ProtoCreateStruct = Google.Api.Expr.V1Alpha1.Expr.Types.CreateStruct;

namespace Cel2Sql.Cel;

/// <summary>
/// A wrapper over a CEL AST expression node (proto <c>Expr</c>) that presents the same
/// accessor surface as dev.cel's <c>CelExpr</c>, so the converter is a mechanical translation.
/// </summary>
public sealed class CelExprNode
{
    private readonly ProtoExpr _expr;

    internal CelExprNode(ProtoExpr expr) => _expr = expr;

    /// <summary>Unique node id, used for type lookups via <see cref="CelAst.GetType"/>.</summary>
    public long Id => _expr.Id;

    /// <summary>The kind of this expression.</summary>
    public CelExprKind Kind => _expr.ExprKindCase switch
    {
        ProtoExpr.ExprKindOneofCase.ConstExpr => CelExprKind.Constant,
        ProtoExpr.ExprKindOneofCase.IdentExpr => CelExprKind.Ident,
        ProtoExpr.ExprKindOneofCase.SelectExpr => CelExprKind.Select,
        ProtoExpr.ExprKindOneofCase.CallExpr => CelExprKind.Call,
        ProtoExpr.ExprKindOneofCase.ListExpr => CelExprKind.List,
        ProtoExpr.ExprKindOneofCase.ComprehensionExpr => CelExprKind.Comprehension,
        ProtoExpr.ExprKindOneofCase.StructExpr =>
            string.IsNullOrEmpty(_expr.StructExpr.MessageName) ? CelExprKind.Map : CelExprKind.Struct,
        _ => CelExprKind.Unspecified,
    };

    public CelCallNode Call() => new(_expr.CallExpr);
    public CelConstantNode Constant() => new(_expr.ConstExpr);
    public CelIdentNode Ident() => new(_expr.IdentExpr);
    public CelSelectNode Select() => new(_expr.SelectExpr);
    public CelListNode List() => new(_expr.ListExpr);
    public CelStructNode Struct() => new(_expr.StructExpr);
    public CelMapNode Map() => new(_expr.StructExpr);
    public CelComprehensionNode Comprehension() => new(_expr.ComprehensionExpr);

    internal static IReadOnlyList<CelExprNode> WrapAll(IEnumerable<ProtoExpr> exprs)
    {
        var list = new List<CelExprNode>();
        foreach (var e in exprs) list.Add(new CelExprNode(e));
        return list;
    }
}

/// <summary>A function call or operator (mirrors CelExpr.CelCall).</summary>
public sealed class CelCallNode
{
    private readonly ProtoExpr.Types.Call _call;
    internal CelCallNode(ProtoExpr.Types.Call call) => _call = call;

    /// <summary>The operator/function name (e.g. "_+_", "contains", "@in").</summary>
    public string Function => _call.Function;

    /// <summary>The receiver for method-style calls (null for global calls).</summary>
    public CelExprNode? Target => _call.Target != null ? new CelExprNode(_call.Target) : null;

    /// <summary>The call arguments.</summary>
    public IReadOnlyList<CelExprNode> Args => CelExprNode.WrapAll(_call.Args);
}

/// <summary>A literal constant (mirrors CelConstant).</summary>
public sealed class CelConstantNode
{
    private readonly ProtoConstant _c;
    internal CelConstantNode(ProtoConstant c) => _c = c;

    public CelConstantKind Kind => _c.ConstantKindCase switch
    {
        ProtoConstant.ConstantKindOneofCase.NullValue => CelConstantKind.NullValue,
        ProtoConstant.ConstantKindOneofCase.BoolValue => CelConstantKind.BooleanValue,
        ProtoConstant.ConstantKindOneofCase.Int64Value => CelConstantKind.Int64Value,
        ProtoConstant.ConstantKindOneofCase.Uint64Value => CelConstantKind.Uint64Value,
        ProtoConstant.ConstantKindOneofCase.DoubleValue => CelConstantKind.DoubleValue,
        ProtoConstant.ConstantKindOneofCase.StringValue => CelConstantKind.StringValue,
        ProtoConstant.ConstantKindOneofCase.BytesValue => CelConstantKind.BytesValue,
        ProtoConstant.ConstantKindOneofCase.DurationValue => CelConstantKind.DurationValue,
        ProtoConstant.ConstantKindOneofCase.TimestampValue => CelConstantKind.TimestampValue,
        _ => CelConstantKind.NullValue,
    };

    public bool BooleanValue => _c.BoolValue;
    public long Int64Value => _c.Int64Value;
    public ulong Uint64Value => _c.Uint64Value;
    public double DoubleValue => _c.DoubleValue;
    public string StringValue => _c.StringValue;
    public byte[] BytesValue => _c.BytesValue.ToByteArray();
}

/// <summary>An identifier reference (mirrors CelExpr.Ident).</summary>
public sealed class CelIdentNode
{
    private readonly ProtoExpr.Types.Ident _ident;
    internal CelIdentNode(ProtoExpr.Types.Ident ident) => _ident = ident;
    public string Name => _ident.Name;
}

/// <summary>A field selection (mirrors CelExpr.CelSelect).</summary>
public sealed class CelSelectNode
{
    private readonly ProtoExpr.Types.Select _select;
    internal CelSelectNode(ProtoExpr.Types.Select select) => _select = select;
    public CelExprNode Operand => new(_select.Operand);
    public string Field => _select.Field;
    public bool TestOnly => _select.TestOnly;
}

/// <summary>A list literal (mirrors CelExpr.CelList).</summary>
public sealed class CelListNode
{
    private readonly ProtoExpr.Types.CreateList _list;
    internal CelListNode(ProtoExpr.Types.CreateList list) => _list = list;
    public IReadOnlyList<CelExprNode> Elements => CelExprNode.WrapAll(_list.Elements);
}

/// <summary>A struct/message literal (mirrors CelExpr.CelStruct).</summary>
public sealed class CelStructNode
{
    private readonly ProtoCreateStruct _struct;
    internal CelStructNode(ProtoCreateStruct s) => _struct = s;
    public string MessageName => _struct.MessageName;
    public IReadOnlyList<CelStructEntry> Entries
    {
        get
        {
            var list = new List<CelStructEntry>();
            foreach (var e in _struct.Entries) list.Add(new CelStructEntry(e));
            return list;
        }
    }
}

/// <summary>An entry of a struct literal.</summary>
public sealed class CelStructEntry
{
    private readonly ProtoCreateStruct.Types.Entry _entry;
    internal CelStructEntry(ProtoCreateStruct.Types.Entry entry) => _entry = entry;
    public string FieldKey => _entry.FieldKey;
    public CelExprNode Value => new(_entry.Value);
}

/// <summary>A map literal (mirrors CelExpr.CelMap).</summary>
public sealed class CelMapNode
{
    private readonly ProtoCreateStruct _struct;
    internal CelMapNode(ProtoCreateStruct s) => _struct = s;
    public IReadOnlyList<CelMapEntry> Entries
    {
        get
        {
            var list = new List<CelMapEntry>();
            foreach (var e in _struct.Entries) list.Add(new CelMapEntry(e));
            return list;
        }
    }
}

/// <summary>An entry of a map literal (key + value).</summary>
public sealed class CelMapEntry
{
    private readonly ProtoCreateStruct.Types.Entry _entry;
    internal CelMapEntry(ProtoCreateStruct.Types.Entry entry) => _entry = entry;
    public CelExprNode Key => new(_entry.MapKey);
    public CelExprNode Value => new(_entry.Value);
}

/// <summary>A comprehension (all/exists/exists_one/map/filter) (mirrors CelExpr.CelComprehension).</summary>
public sealed class CelComprehensionNode
{
    private readonly ProtoExpr.Types.Comprehension _comp;
    internal CelComprehensionNode(ProtoExpr.Types.Comprehension comp) => _comp = comp;
    public string IterVar => _comp.IterVar;
    public CelExprNode IterRange => new(_comp.IterRange);
    public string AccuVar => _comp.AccuVar;
    public CelExprNode AccuInit => new(_comp.AccuInit);
    public CelExprNode LoopCondition => new(_comp.LoopCondition);
    public CelExprNode LoopStep => new(_comp.LoopStep);
    public CelExprNode Result => new(_comp.Result);
}
