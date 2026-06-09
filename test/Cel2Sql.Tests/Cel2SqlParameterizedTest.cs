using FluentAssertions;
using Xunit;

namespace Cel2Sql.Tests;

/// <summary>
/// Parameterized query tests: literals extracted into positional parameters while booleans
/// and nulls stay inlined. Mirrors Cel2SqlParameterizedTest.java.
/// (M1: PostgreSQL placeholder style $N; M3 adds ?, @pN dialects.)
/// </summary>
public class Cel2SqlParameterizedTest
{
    public static IEnumerable<object[]> ParameterizedCases()
    {
        yield return Case("simple_string_equality", "name == \"John\"", "name = $1", new object?[] { "John" });
        yield return Case("multiple_params", "name == \"John\" && name != \"Jane\"",
            "name = $1 AND name != $2", new object?[] { "John", "Jane" });
        yield return Case("integer_equality", "age == 18", "age = $1", new object?[] { 18L });
        yield return Case("double_equality", "height == 1.75", "height = $1", new object?[] { 1.75 });
        yield return Case("boolean_inline_true", "active == true", "active IS TRUE", Array.Empty<object?>());
        yield return Case("boolean_inline_false", "active == false", "active IS FALSE", Array.Empty<object?>());
        yield return Case("null_inline", "null_var == null", "null_var IS NULL", Array.Empty<object?>());
        yield return Case("mixed_params_and_inlined", "name == \"Alice\" && active == true && age > 21",
            "name = $1 AND active IS TRUE AND age > $2", new object?[] { "Alice", 21L });
    }

    private static object[] Case(string name, string cel, string sql, object?[] parameters) =>
        new object[] { name, cel, TestDialects.PostgreSql, sql, parameters };

    [Theory]
    [MemberData(nameof(ParameterizedCases))]
    public void Parameterized(string name, string celExpr, string dialectName, string expectedSql, object?[] expectedParams)
    {
        var ast = CelTestEnv.Compile(celExpr);
        var result = Cel2SqlConverter.ConvertParameterized(ast, o => o.WithDialect(TestDialects.Get(dialectName)));
        result.Sql.Should().Be(expectedSql, "SQL for {0} [{1}]", name, dialectName);
        result.Parameters.Should().Equal(expectedParams, "Parameters for {0} [{1}]", name, dialectName);
    }
}
