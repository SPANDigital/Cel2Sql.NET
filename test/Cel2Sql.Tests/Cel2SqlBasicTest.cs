using FluentAssertions;
using Xunit;

namespace Cel2Sql.Tests;

/// <summary>
/// Basic conversion tests covering equality, comparison, null handling, boolean
/// logic, negation, and ternary expressions. Mirrors Cel2SqlBasicTest.java.
/// These cases produce identical SQL across all dialects.
/// </summary>
public class Cel2SqlBasicTest
{
    // (name, celExpr, expectedSql) — same expected SQL for every dialect.
    private static readonly (string Name, string Cel, string Sql)[] Cases =
    {
        ("equality_string", "name == \"a\"", "name = 'a'"),
        ("inequality_int", "age != 20", "age != 20"),
        ("less_than", "age < 20", "age < 20"),
        ("less_equal", "age <= 20", "age <= 20"),
        ("greater_than", "age > 20", "age > 20"),
        ("greater_equal_float", "height >= 1.6180339887", "height >= 1.6180339887"),
        ("is_null", "null_var == null", "null_var IS NULL"),
        ("is_not_null", "null_var != null", "null_var IS NOT NULL"),
        ("is_true", "adult == true", "adult IS TRUE"),
        ("is_not_true", "adult != true", "adult IS NOT TRUE"),
        ("is_false", "adult == false", "adult IS FALSE"),
        ("is_not_false", "adult != false", "adult IS NOT FALSE"),
        ("not", "!adult", "NOT adult"),
        ("negative_int", "-1", "-1"),
        ("negative_var", "-age", "-age"),
        ("ternary", "name == \"a\" ? \"a\" : \"b\"", "CASE WHEN name = 'a' THEN 'a' ELSE 'b' END"),
        ("field_select", "page.title == \"test\"", "page.title = 'test'"),
        ("string_literal", "\"hello\"", "'hello'"),
        ("int_literal", "42", "42"),
        ("double_literal", "3.14", "3.14"),
        ("bool_true", "true", "TRUE"),
        ("bool_false", "false", "FALSE"),
    };

    public static IEnumerable<object[]> BasicCases()
    {
        foreach (var (name, cel, sql) in Cases)
            foreach (var dialect in TestDialects.FiveSqlDialects)
                yield return new object[] { name, cel, dialect, sql };
    }

    [Theory]
    [MemberData(nameof(BasicCases))]
    public void Basic(string name, string celExpr, string dialectName, string expectedSql)
    {
        var ast = CelTestEnv.Compile(celExpr);
        var sql = Cel2SqlConverter.Convert(ast, o => o.WithDialect(TestDialects.Get(dialectName)));
        sql.Should().Be(expectedSql, "{0}: CEL '{1}' [{2}]", name, celExpr, dialectName);
    }
}
