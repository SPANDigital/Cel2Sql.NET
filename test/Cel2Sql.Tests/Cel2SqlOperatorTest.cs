using FluentAssertions;
using Xunit;

namespace Cel2Sql.Tests;

/// <summary>
/// Operator tests: logical AND/OR, arithmetic, parenthesization, string concatenation.
/// Mirrors Cel2SqlOperatorTest.java. (M1: PostgreSQL rows; M3 adds the other dialects.)
/// </summary>
public class Cel2SqlOperatorTest
{
    public static IEnumerable<object[]> OperatorCases()
    {
        // Uniform across all five SQL dialects.
        var uniform = new (string Name, string Cel, string Sql)[]
        {
            ("logical_and", "name == \"a\" && age > 20", "name = 'a' AND age > 20"),
            ("logical_or", "name == \"a\" || age > 20", "name = 'a' OR age > 20"),
            ("parenthesized_or_inside_and", "age >= 10 && (name == \"a\" || name == \"b\")",
                "age >= 10 AND (name = 'a' OR name = 'b')"),
            ("parenthesized_and_inside_or", "(age >= 10 && name == \"a\") || name == \"b\"",
                "age >= 10 AND name = 'a' OR name = 'b'"),
            ("addition", "1 + 2 == 3", "1 + 2 = 3"),
            ("subtraction", "10 - 5 == 5", "10 - 5 = 5"),
            ("multiplication", "3 * 4 == 12", "3 * 4 = 12"),
            ("division", "10 / 2 == 5", "10 / 2 = 5"),
            ("modulo", "5 % 3 == 2", "5 % 3 = 2"),
        };
        foreach (var (name, cel, sql) in uniform)
            foreach (var dialect in TestDialects.FiveSqlDialects)
                yield return new object[] { name, cel, dialect, sql };

        // String concatenation: MySQL uses CONCAT(), others use ||.
        var concatByDialect = new (string Dialect, string Sql)[]
        {
            (TestDialects.PostgreSql, "'a' || 'b' = 'ab'"),
            (TestDialects.MySql, "CONCAT('a', 'b') = 'ab'"),
            (TestDialects.Sqlite, "'a' || 'b' = 'ab'"),
            (TestDialects.DuckDb, "'a' || 'b' = 'ab'"),
            (TestDialects.BigQuery, "'a' || 'b' = 'ab'"),
        };
        foreach (var (dialect, sql) in concatByDialect)
            yield return new object[] { "string_concat", "\"a\" + \"b\" == \"ab\"", dialect, sql };
    }

    [Theory]
    [MemberData(nameof(OperatorCases))]
    public void Operator(string name, string celExpr, string dialectName, string expectedSql)
    {
        var ast = CelTestEnv.Compile(celExpr);
        var sql = Cel2SqlConverter.Convert(ast, o => o.WithDialect(TestDialects.Get(dialectName)));
        sql.Should().Be(expectedSql, "{0}: CEL '{1}' [{2}]", name, celExpr, dialectName);
    }
}
