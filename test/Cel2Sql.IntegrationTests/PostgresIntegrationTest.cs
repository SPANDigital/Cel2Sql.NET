using Cel2Sql.Dialects.Postgres;
using Cel2Sql.IntegrationTests.Fixtures;
using Xunit;

namespace Cel2Sql.IntegrationTests;

public sealed class PostgresIntegrationTest : DialectIntegrationTestBase, IClassFixture<PostgresFixture>
{
    public PostgresIntegrationTest(PostgresFixture fixture) : base(fixture) { }

    public static IEnumerable<object[]> SqlCases() => IntegrationCatalog.SqlCasesFor(new PostgresDialect());
    public static IEnumerable<object[]> ParamCases() => IntegrationCatalog.ParamCasesFor(new PostgresDialect());

    [Theory]
    [MemberData(nameof(SqlCases))]
    public void Sql(string name, string cel, int[] expectedRowIds, bool expressionOnly, string category)
        => RunSqlCase(name, cel, expectedRowIds, expressionOnly, category);

    [Theory]
    [MemberData(nameof(ParamCases))]
    public void Parameterized(string name, string cel, int[] expectedRowIds, bool expressionOnly, string category)
        => RunParamCase(name, cel, expectedRowIds, expressionOnly, category);
}
