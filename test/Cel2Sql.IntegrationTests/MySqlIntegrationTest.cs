using Cel2Sql.Dialects.MySql;
using Cel2Sql.IntegrationTests.Fixtures;
using Xunit;

namespace Cel2Sql.IntegrationTests;

public sealed class MySqlIntegrationTest : DialectIntegrationTestBase, IClassFixture<MySqlFixture>
{
    public MySqlIntegrationTest(MySqlFixture fixture) : base(fixture) { }

    public static IEnumerable<object[]> SqlCases() => IntegrationCatalog.SqlCasesFor(new MySqlDialect());
    public static IEnumerable<object[]> ParamCases() => IntegrationCatalog.ParamCasesFor(new MySqlDialect());

    [Theory]
    [MemberData(nameof(SqlCases))]
    public void Sql(string name, string cel, int[] expectedRowIds, bool expressionOnly, string category)
        => RunSqlCase(name, cel, expectedRowIds, expressionOnly, category);

    [Theory]
    [MemberData(nameof(ParamCases))]
    public void Parameterized(string name, string cel, int[] expectedRowIds, bool expressionOnly, string category)
        => RunParamCase(name, cel, expectedRowIds, expressionOnly, category);
}
