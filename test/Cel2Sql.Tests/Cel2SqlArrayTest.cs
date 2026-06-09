using FluentAssertions;
using Xunit;

namespace Cel2Sql.Tests;

/// <summary>
/// Array/list tests covering list indexing, size, and the IN operator.
/// Mirrors Cel2SqlArrayTest.java (PostgreSQL rows only).
/// </summary>
public class Cel2SqlArrayTest
{
    public static IEnumerable<object[]> ArrayCases()
    {
        yield return Case("list_index_literal", "[1, 2, 3][0] == 1", "(ARRAY[1, 2, 3])[1] = 1");
        yield return Case("size_list", "size(string_list)", "COALESCE(ARRAY_LENGTH(string_list, 1), 0)");
        yield return Case("in_list", "name in [\"a\", \"b\", \"c\"]", "name = ANY(ARRAY['a', 'b', 'c'])");
        yield return Case("size_list_var_method", "string_list.size()", "COALESCE(ARRAY_LENGTH(string_list, 1), 0)");
    }

    private static object[] Case(string name, string cel, string sql) =>
        new object[] { name, cel, TestDialects.PostgreSql, sql };

    [Theory]
    [MemberData(nameof(ArrayCases))]
    public void Array(string name, string celExpr, string dialectName, string expectedSql)
    {
        var ast = CelTestEnv.Compile(celExpr);
        var sql = Cel2SqlConverter.Convert(ast, o => o.WithDialect(TestDialects.Get(dialectName)));
        sql.Should().Be(expectedSql, "{0}: CEL '{1}' [{2}]", name, celExpr, dialectName);
    }
}
