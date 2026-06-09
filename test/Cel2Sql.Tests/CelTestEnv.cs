using Cel2Sql.Cel;

namespace Cel2Sql.Tests;

/// <summary>
/// Helper for compiling CEL expressions in tests. Provides a standard environment
/// pre-configured with common variable declarations (mirrors the Java CelHelper).
/// </summary>
public static class CelTestEnv
{
    private static readonly CelEnvironment Standard = BuildStandard();

    /// <summary>Builds the standard environment with the common test variable declarations.</summary>
    public static CelEnvironment BuildStandard() =>
        CelEnvironment.NewBuilder()
            .AddVariable("name", CelVarType.String)
            .AddVariable("age", CelVarType.Int)
            .AddVariable("adult", CelVarType.Bool)
            .AddVariable("height", CelVarType.Double)
            .AddVariable("email", CelVarType.String)
            .AddVariable("tags", CelVarType.List(CelVarType.String))
            .AddVariable("scores", CelVarType.List(CelVarType.Int))
            .AddVariable("salary", CelVarType.Double)
            .AddVariable("active", CelVarType.Bool)
            .AddVariable("null_var", CelVarType.Dyn)
            .AddVariable("string_list", CelVarType.List(CelVarType.String))
            .AddVariable("int_list", CelVarType.List(CelVarType.Int))
            .AddVariable("created_at", CelVarType.Timestamp)
            .AddVariable("page", CelVarType.Map(CelVarType.String, CelVarType.Dyn))
            .Build();

    /// <summary>Compiles a CEL expression string using the standard environment.</summary>
    public static CelAst Compile(string celExpr) => Standard.Compile(celExpr);

    /// <summary>Compiles using a custom environment.</summary>
    public static CelAst Compile(CelEnvironment env, string celExpr) => env.Compile(celExpr);
}
