using AwesomeAssertions;
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
        // simple_string_equality
        yield return Case("simple_string_equality", "name == \"John\"", TestDialects.PostgreSql, "name = $1", new object?[] { "John" });
        yield return Case("simple_string_equality", "name == \"John\"", TestDialects.MySql, "name = ?", new object?[] { "John" });
        yield return Case("simple_string_equality", "name == \"John\"", TestDialects.Sqlite, "name = ?", new object?[] { "John" });
        yield return Case("simple_string_equality", "name == \"John\"", TestDialects.DuckDb, "name = $1", new object?[] { "John" });
        yield return Case("simple_string_equality", "name == \"John\"", TestDialects.BigQuery, "name = @p1", new object?[] { "John" });

        // multiple_params
        yield return Case("multiple_params", "name == \"John\" && name != \"Jane\"", TestDialects.PostgreSql, "name = $1 AND name != $2", new object?[] { "John", "Jane" });
        yield return Case("multiple_params", "name == \"John\" && name != \"Jane\"", TestDialects.MySql, "name = ? AND name != ?", new object?[] { "John", "Jane" });
        yield return Case("multiple_params", "name == \"John\" && name != \"Jane\"", TestDialects.Sqlite, "name = ? AND name != ?", new object?[] { "John", "Jane" });
        yield return Case("multiple_params", "name == \"John\" && name != \"Jane\"", TestDialects.DuckDb, "name = $1 AND name != $2", new object?[] { "John", "Jane" });
        yield return Case("multiple_params", "name == \"John\" && name != \"Jane\"", TestDialects.BigQuery, "name = @p1 AND name != @p2", new object?[] { "John", "Jane" });

        // integer_equality
        yield return Case("integer_equality", "age == 18", TestDialects.PostgreSql, "age = $1", new object?[] { 18L });
        yield return Case("integer_equality", "age == 18", TestDialects.MySql, "age = ?", new object?[] { 18L });
        yield return Case("integer_equality", "age == 18", TestDialects.Sqlite, "age = ?", new object?[] { 18L });
        yield return Case("integer_equality", "age == 18", TestDialects.DuckDb, "age = $1", new object?[] { 18L });
        yield return Case("integer_equality", "age == 18", TestDialects.BigQuery, "age = @p1", new object?[] { 18L });

        // double_equality
        yield return Case("double_equality", "height == 1.75", TestDialects.PostgreSql, "height = $1", new object?[] { 1.75 });
        yield return Case("double_equality", "height == 1.75", TestDialects.MySql, "height = ?", new object?[] { 1.75 });
        yield return Case("double_equality", "height == 1.75", TestDialects.Sqlite, "height = ?", new object?[] { 1.75 });
        yield return Case("double_equality", "height == 1.75", TestDialects.DuckDb, "height = $1", new object?[] { 1.75 });
        yield return Case("double_equality", "height == 1.75", TestDialects.BigQuery, "height = @p1", new object?[] { 1.75 });

        // boolean_inline_true: booleans are never parameterized
        yield return Case("boolean_inline_true", "active == true", TestDialects.PostgreSql, "active IS TRUE", Array.Empty<object?>());
        yield return Case("boolean_inline_true", "active == true", TestDialects.MySql, "active IS TRUE", Array.Empty<object?>());
        yield return Case("boolean_inline_true", "active == true", TestDialects.Sqlite, "active IS TRUE", Array.Empty<object?>());
        yield return Case("boolean_inline_true", "active == true", TestDialects.DuckDb, "active IS TRUE", Array.Empty<object?>());
        yield return Case("boolean_inline_true", "active == true", TestDialects.BigQuery, "active IS TRUE", Array.Empty<object?>());

        // boolean_inline_false: booleans are never parameterized
        yield return Case("boolean_inline_false", "active == false", TestDialects.PostgreSql, "active IS FALSE", Array.Empty<object?>());
        yield return Case("boolean_inline_false", "active == false", TestDialects.MySql, "active IS FALSE", Array.Empty<object?>());
        yield return Case("boolean_inline_false", "active == false", TestDialects.Sqlite, "active IS FALSE", Array.Empty<object?>());
        yield return Case("boolean_inline_false", "active == false", TestDialects.DuckDb, "active IS FALSE", Array.Empty<object?>());
        yield return Case("boolean_inline_false", "active == false", TestDialects.BigQuery, "active IS FALSE", Array.Empty<object?>());

        // null_inline: nulls are never parameterized
        yield return Case("null_inline", "null_var == null", TestDialects.PostgreSql, "null_var IS NULL", Array.Empty<object?>());
        yield return Case("null_inline", "null_var == null", TestDialects.MySql, "null_var IS NULL", Array.Empty<object?>());
        yield return Case("null_inline", "null_var == null", TestDialects.Sqlite, "null_var IS NULL", Array.Empty<object?>());
        yield return Case("null_inline", "null_var == null", TestDialects.DuckDb, "null_var IS NULL", Array.Empty<object?>());
        yield return Case("null_inline", "null_var == null", TestDialects.BigQuery, "null_var IS NULL", Array.Empty<object?>());

        // mixed_params_and_inlined
        yield return Case("mixed_params_and_inlined", "name == \"Alice\" && active == true && age > 21", TestDialects.PostgreSql, "name = $1 AND active IS TRUE AND age > $2", new object?[] { "Alice", 21L });
        yield return Case("mixed_params_and_inlined", "name == \"Alice\" && active == true && age > 21", TestDialects.MySql, "name = ? AND active IS TRUE AND age > ?", new object?[] { "Alice", 21L });
        yield return Case("mixed_params_and_inlined", "name == \"Alice\" && active == true && age > 21", TestDialects.Sqlite, "name = ? AND active IS TRUE AND age > ?", new object?[] { "Alice", 21L });
        yield return Case("mixed_params_and_inlined", "name == \"Alice\" && active == true && age > 21", TestDialects.DuckDb, "name = $1 AND active IS TRUE AND age > $2", new object?[] { "Alice", 21L });
        yield return Case("mixed_params_and_inlined", "name == \"Alice\" && active == true && age > 21", TestDialects.BigQuery, "name = @p1 AND active IS TRUE AND age > @p2", new object?[] { "Alice", 21L });
    }

    private static object[] Case(string name, string cel, string dialectName, string sql, object?[] parameters) =>
        new object[] { name, cel, dialectName, sql, parameters };

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
