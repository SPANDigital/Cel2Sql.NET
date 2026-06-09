using FluentAssertions;
using Xunit;

namespace Cel2Sql.Tests;

/// <summary>
/// Comprehension tests covering all(), exists(), exists_one(), filter(), and map()
/// macros on lists across PostgreSQL, SQLite, DuckDB, and BigQuery.
/// MySQL is skipped (no comprehension support in the Go reference).
/// Mirrors Cel2SqlComprehensionTest.java.
/// </summary>
public class Cel2SqlComprehensionTest
{
    public static IEnumerable<object[]> ComprehensionCases()
    {
        // all: NOT EXISTS with UNNEST/json_each
        yield return Case("all", "string_list.all(x, x != \"bad\")", TestDialects.PostgreSql,
            "NOT EXISTS (SELECT 1 FROM UNNEST(string_list) AS x WHERE NOT (x != 'bad'))");
        yield return Case("all", "string_list.all(x, x != \"bad\")", TestDialects.Sqlite,
            "NOT EXISTS (SELECT 1 FROM (SELECT value AS x FROM json_each(string_list)) AS _t WHERE NOT (x != 'bad'))");
        yield return Case("all", "string_list.all(x, x != \"bad\")", TestDialects.DuckDb,
            "NOT EXISTS (SELECT 1 FROM UNNEST(string_list) AS _t(x) WHERE NOT (x != 'bad'))");
        yield return Case("all", "string_list.all(x, x != \"bad\")", TestDialects.BigQuery,
            "NOT EXISTS (SELECT 1 FROM UNNEST(string_list) AS x WHERE NOT (x != 'bad'))");

        // exists: EXISTS with UNNEST/json_each
        yield return Case("exists", "string_list.exists(x, x == \"good\")", TestDialects.PostgreSql,
            "EXISTS (SELECT 1 FROM UNNEST(string_list) AS x WHERE x = 'good')");
        yield return Case("exists", "string_list.exists(x, x == \"good\")", TestDialects.Sqlite,
            "EXISTS (SELECT 1 FROM (SELECT value AS x FROM json_each(string_list)) AS _t WHERE x = 'good')");
        yield return Case("exists", "string_list.exists(x, x == \"good\")", TestDialects.DuckDb,
            "EXISTS (SELECT 1 FROM UNNEST(string_list) AS _t(x) WHERE x = 'good')");
        yield return Case("exists", "string_list.exists(x, x == \"good\")", TestDialects.BigQuery,
            "EXISTS (SELECT 1 FROM UNNEST(string_list) AS x WHERE x = 'good')");

        // exists_one: COUNT subquery
        yield return Case("exists_one", "string_list.exists_one(x, x == \"unique\")", TestDialects.PostgreSql,
            "(SELECT COUNT(*) FROM UNNEST(string_list) AS x WHERE x = 'unique') = 1");
        yield return Case("exists_one", "string_list.exists_one(x, x == \"unique\")", TestDialects.Sqlite,
            "(SELECT COUNT(*) FROM (SELECT value AS x FROM json_each(string_list)) AS _t WHERE x = 'unique') = 1");
        yield return Case("exists_one", "string_list.exists_one(x, x == \"unique\")", TestDialects.DuckDb,
            "(SELECT COUNT(*) FROM UNNEST(string_list) AS _t(x) WHERE x = 'unique') = 1");
        yield return Case("exists_one", "string_list.exists_one(x, x == \"unique\")", TestDialects.BigQuery,
            "(SELECT COUNT(*) FROM UNNEST(string_list) AS x WHERE x = 'unique') = 1");

        // filter: ARRAY subquery / json_group_array
        yield return Case("filter", "string_list.filter(x, x != \"bad\")", TestDialects.PostgreSql,
            "ARRAY(SELECT x FROM UNNEST(string_list) AS x WHERE x != 'bad')");
        yield return Case("filter", "string_list.filter(x, x != \"bad\")", TestDialects.Sqlite,
            "(SELECT json_group_array(x) FROM (SELECT value AS x FROM json_each(string_list)) AS _t WHERE x != 'bad')");
        yield return Case("filter", "string_list.filter(x, x != \"bad\")", TestDialects.DuckDb,
            "ARRAY(SELECT x FROM UNNEST(string_list) AS _t(x) WHERE x != 'bad')");
        yield return Case("filter", "string_list.filter(x, x != \"bad\")", TestDialects.BigQuery,
            "ARRAY(SELECT x FROM UNNEST(string_list) AS x WHERE x != 'bad')");

        // map_transform: ARRAY subquery with transform / json_group_array
        yield return Case("map_transform", "string_list.map(x, x + \"_suffix\")", TestDialects.PostgreSql,
            "ARRAY(SELECT x || '_suffix' FROM UNNEST(string_list) AS x)");
        yield return Case("map_transform", "string_list.map(x, x + \"_suffix\")", TestDialects.Sqlite,
            "(SELECT json_group_array(x || '_suffix') FROM (SELECT value AS x FROM json_each(string_list)) AS _t)");
        yield return Case("map_transform", "string_list.map(x, x + \"_suffix\")", TestDialects.DuckDb,
            "ARRAY(SELECT x || '_suffix' FROM UNNEST(string_list) AS _t(x))");
        yield return Case("map_transform", "string_list.map(x, x + \"_suffix\")", TestDialects.BigQuery,
            "ARRAY(SELECT x || '_suffix' FROM UNNEST(string_list) AS x)");
    }

    private static object[] Case(string name, string cel, string dialectName, string sql) =>
        new object[] { name, cel, dialectName, sql };

    [Theory]
    [MemberData(nameof(ComprehensionCases))]
    public void Comprehension(string name, string celExpr, string dialectName, string expectedSql)
    {
        var ast = CelTestEnv.Compile(celExpr);
        var sql = Cel2SqlConverter.Convert(ast, o => o.WithDialect(TestDialects.Get(dialectName)));
        sql.Should().Be(expectedSql, "{0}: CEL '{1}' [{2}]", name, celExpr, dialectName);
    }
}
