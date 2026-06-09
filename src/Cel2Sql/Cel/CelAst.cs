using CelNet = global::Cel;
using ProtoType = Google.Api.Expr.V1Alpha1.Type;

namespace Cel2Sql.Cel;

/// <summary>
/// A checked CEL abstract syntax tree: the root expression plus per-node type information.
/// Equivalent to dev.cel's <c>CelAbstractSyntaxTree</c>.
/// </summary>
public sealed class CelAst
{
    private readonly Dictionary<long, CelTypeRef> _typeMap;

    /// <summary>The root expression node.</summary>
    public CelExprNode Expr { get; }

    private CelAst(CelExprNode expr, Dictionary<long, CelTypeRef> typeMap)
    {
        Expr = expr;
        _typeMap = typeMap;
    }

    /// <summary>
    /// Returns the checked type of the node with the given id, or null if unknown.
    /// Equivalent to <c>ast.getType(exprId).orElse(null)</c>.
    /// </summary>
    public CelTypeRef? GetType(long exprId) =>
        _typeMap.TryGetValue(exprId, out var t) ? t : null;

    /// <summary>
    /// Builds a <see cref="CelAst"/> from a Cel.NET checked <c>Ast</c>. The AST must already be
    /// type-checked (via <c>env.Compile</c> or <c>env.Check</c>); the per-node type map is read
    /// from the proto <c>CheckedExpr</c>.
    /// </summary>
    public static CelAst FromCelNet(CelNet.Ast ast)
    {
        var checkedExpr = CelNet.Cel.AstToCheckedExpr(ast);
        var typeMap = new Dictionary<long, CelTypeRef>(checkedExpr.TypeMap.Count);
        foreach (var kv in checkedExpr.TypeMap)
        {
            typeMap[kv.Key] = CelTypeRef.From(kv.Value);
        }
        return new CelAst(new CelExprNode(checkedExpr.Expr), typeMap);
    }
}
