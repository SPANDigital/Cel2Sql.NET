namespace Cel2Sql.IntegrationTests;

/// <summary>
/// Describes a single integration test case: a CEL expression, the category it belongs to,
/// and either expected row IDs (for WHERE clauses) or expression-only mode.
/// Ports the Java <c>IntegrationTestCase</c> record.
/// </summary>
public sealed record IntegrationTestCase(
    string Name,
    string Cel,
    int[] ExpectedRowIds,
    bool ExpressionOnly,
    string Category)
{
    /// <summary>Creates a WHERE-clause test case that checks specific row IDs are returned.</summary>
    public static IntegrationTestCase Where(string name, string cel, string category, params int[] ids) =>
        new(name, cel, ids, false, category);

    /// <summary>Creates an expression-only test case that just verifies the SQL executes without error.</summary>
    public static IntegrationTestCase Expr(string name, string cel, string category) =>
        new(name, cel, Array.Empty<int>(), true, category);
}
