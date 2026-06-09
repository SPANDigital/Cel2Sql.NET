using FluentAssertions;
using Xunit;

namespace Cel2Sql.Tests;

/// <summary>
/// Comprehension tests covering all(), exists(), exists_one(), filter(), and map()
/// macros on lists. Mirrors Cel2SqlComprehensionTest.java (PostgreSQL rows only).
/// </summary>
public class Cel2SqlComprehensionTest
{
    public static IEnumerable<object[]> ComprehensionCases()
    {
        yield return Case("all", "string_list.all(x, x != \"bad\")",
            "NOT EXISTS (SELECT 1 FROM UNNEST(string_list) AS x WHERE NOT (x != 'bad'))");
        yield return Case("exists", "string_list.exists(x, x == \"good\")",
            "EXISTS (SELECT 1 FROM UNNEST(string_list) AS x WHERE x = 'good')");
        yield return Case("exists_one", "string_list.exists_one(x, x == \"unique\")",
            "(SELECT COUNT(*) FROM UNNEST(string_list) AS x WHERE x = 'unique') = 1");
        yield return Case("filter", "string_list.filter(x, x != \"bad\")",
            "ARRAY(SELECT x FROM UNNEST(string_list) AS x WHERE x != 'bad')");
        yield return Case("map_transform", "string_list.map(x, x + \"_suffix\")",
            "ARRAY(SELECT x || '_suffix' FROM UNNEST(string_list) AS x)");
    }

    private static object[] Case(string name, string cel, string sql) =>
        new object[] { name, cel, TestDialects.PostgreSql, sql };

    [Theory]
    [MemberData(nameof(ComprehensionCases))]
    public void Comprehension(string name, string celExpr, string dialectName, string expectedSql)
    {
        var ast = CelTestEnv.Compile(celExpr);
        var sql = Cel2SqlConverter.Convert(ast, o => o.WithDialect(TestDialects.Get(dialectName)));
        sql.Should().Be(expectedSql, "{0}: CEL '{1}' [{2}]", name, celExpr, dialectName);
    }
}
