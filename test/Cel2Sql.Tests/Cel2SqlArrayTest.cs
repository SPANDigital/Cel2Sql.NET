using AwesomeAssertions;
using Xunit;

namespace Cel2Sql.Tests;

/// <summary>
/// Array/list tests covering list indexing, size, and the IN operator across
/// supported dialects. MySQL/SQLite are skipped for array indexing and size
/// (no native array support). IN-list is tested across all 5 dialects.
/// Mirrors Cel2SqlArrayTest.java.
/// </summary>
public class Cel2SqlArrayTest
{
    public static IEnumerable<object[]> ArrayCases()
    {
        // list_index_literal: array indexing only for PG/DuckDB/BQ
        yield return Case("list_index_literal", "[1, 2, 3][0] == 1", TestDialects.PostgreSql, "(ARRAY[1, 2, 3])[1] = 1");
        yield return Case("list_index_literal", "[1, 2, 3][0] == 1", TestDialects.DuckDb, "[1, 2, 3][1] = 1");
        yield return Case("list_index_literal", "[1, 2, 3][0] == 1", TestDialects.BigQuery, "[1, 2, 3][OFFSET(0)] = 1");

        // size_list: array size only for PG/DuckDB/BQ
        yield return Case("size_list", "size(string_list)", TestDialects.PostgreSql, "COALESCE(ARRAY_LENGTH(string_list, 1), 0)");
        yield return Case("size_list", "size(string_list)", TestDialects.DuckDb, "COALESCE(array_length(string_list), 0)");
        yield return Case("size_list", "size(string_list)", TestDialects.BigQuery, "COALESCE(ARRAY_LENGTH(string_list), 0)");

        // in_list: all 5 dialects with different containment syntax
        yield return Case("in_list", "name in [\"a\", \"b\", \"c\"]", TestDialects.PostgreSql, "name = ANY(ARRAY['a', 'b', 'c'])");
        yield return Case("in_list", "name in [\"a\", \"b\", \"c\"]", TestDialects.MySql, "JSON_OVERLAPS(JSON_ARRAY(name), JSON_ARRAY('a', 'b', 'c'))");
        yield return Case("in_list", "name in [\"a\", \"b\", \"c\"]", TestDialects.Sqlite, "name IN (SELECT value FROM json_each(json_array('a', 'b', 'c')))");
        yield return Case("in_list", "name in [\"a\", \"b\", \"c\"]", TestDialects.DuckDb, "name = ANY(['a', 'b', 'c'])");
        yield return Case("in_list", "name in [\"a\", \"b\", \"c\"]", TestDialects.BigQuery, "name IN UNNEST(['a', 'b', 'c'])");

        // size_list_var_method: array size via .size() method
        yield return Case("size_list_var_method", "string_list.size()", TestDialects.PostgreSql, "COALESCE(ARRAY_LENGTH(string_list, 1), 0)");
        yield return Case("size_list_var_method", "string_list.size()", TestDialects.DuckDb, "COALESCE(array_length(string_list), 0)");
        yield return Case("size_list_var_method", "string_list.size()", TestDialects.BigQuery, "COALESCE(ARRAY_LENGTH(string_list), 0)");
    }

    private static object[] Case(string name, string cel, string dialectName, string sql) =>
        new object[] { name, cel, dialectName, sql };

    [Theory]
    [MemberData(nameof(ArrayCases))]
    public void Array(string name, string celExpr, string dialectName, string expectedSql)
    {
        var ast = CelTestEnv.Compile(celExpr);
        var sql = Cel2SqlConverter.Convert(ast, o => o.WithDialect(TestDialects.Get(dialectName)));
        sql.Should().Be(expectedSql, "{0}: CEL '{1}' [{2}]", name, celExpr, dialectName);
    }
}
