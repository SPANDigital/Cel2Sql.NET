using System.Linq;
using Cel;
using Cel.Checker;
using Cel2Sql.Errors;
using Google.Api.Expr.V1Alpha1;

namespace Cel2Sql.Cel;

/// <summary>
/// A CEL compilation environment: declared variables + the standard library/macros.
/// Compiles (parses and type-checks) CEL source into a <see cref="CelAst"/>.
/// Wraps Cel.NET's <c>Env</c>.
/// </summary>
public sealed class CelEnvironment
{
    private readonly Env _env;

    internal CelEnvironment(Env env) => _env = env;

    /// <summary>Starts building a new environment.</summary>
    public static CelEnvironmentBuilder NewBuilder() => new();

    /// <summary>
    /// Parses and type-checks a CEL expression, returning the checked AST.
    /// Throws <see cref="ConversionException"/> if compilation fails.
    /// </summary>
    public CelAst Compile(string celExpr)
    {
        var result = _env.Compile(celExpr);
        if (result.Ast == null || (result.Issues != null && result.Issues.HasIssues()))
        {
            throw ConversionException.Of(
                "Failed to compile CEL expression",
                "CEL compilation failed for '" + celExpr + "': " + result.Issues);
        }
        return CelAst.FromCelNet(result.Ast);
    }
}

/// <summary>Builder for a <see cref="CelEnvironment"/>.</summary>
public sealed class CelEnvironmentBuilder
{
    private readonly List<Decl> _decls = new();

    /// <summary>Declares a variable with the given name and type.</summary>
    public CelEnvironmentBuilder AddVariable(string name, CelVarType type)
    {
        _decls.Add(Decls.NewVar(name, type.Proto));
        return this;
    }

    /// <summary>
    /// Declares a member (receiver-style) function: <c>target.name(args...)</c>.
    /// The first element of <paramref name="argTypes"/> is the receiver type.
    /// </summary>
    public CelEnvironmentBuilder AddMemberFunction(
        string name, string overloadId, CelVarType resultType, params CelVarType[] argTypes)
    {
        var protoArgs = argTypes.Select(a => a.Proto).ToList();
        var overload = Decls.NewInstanceOverload(overloadId, protoArgs, resultType.Proto);
        _decls.Add(Decls.NewFunction(name, new[] { overload }));
        return this;
    }

    /// <summary>Builds the environment. The standard library and macros are included by default.</summary>
    public CelEnvironment Build() =>
        new(Env.NewEnv(new[] { EnvOptions.Declarations(_decls.ToArray()) }));
}
