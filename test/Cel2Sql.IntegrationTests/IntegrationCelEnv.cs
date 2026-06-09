using Cel2Sql.Cel;

namespace Cel2Sql.IntegrationTests;

/// <summary>
/// Standard CEL environment for integration tests. Mirrors the variable declarations in
/// <c>Cel2Sql.Tests.CelTestEnv</c> so that the catalog expressions compile.
/// </summary>
public static class IntegrationCelEnv
{
    private static readonly CelEnvironment Standard = BuildStandard();

    private static CelEnvironment BuildStandard() =>
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

    /// <summary>Compiles a CEL expression using the standard environment.</summary>
    public static CelAst Compile(string celExpr) => Standard.Compile(celExpr);
}
