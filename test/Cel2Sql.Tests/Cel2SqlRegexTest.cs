using FluentAssertions;
using Xunit;

namespace Cel2Sql.Tests;

/// <summary>
/// Regex matching tests covering simple patterns, word boundary conversion,
/// and character class conversion across PostgreSQL, MySQL, DuckDB, and BigQuery.
/// SQLite is skipped as it does not support regex.
/// Mirrors Cel2SqlRegexTest.java.
/// </summary>
public class Cel2SqlRegexTest
{
    public static IEnumerable<object[]> RegexCases()
    {
        // simple_match: regex operator syntax differs per dialect
        yield return Case("simple_match", "name.matches(\"a+\")", TestDialects.PostgreSql, "name ~ 'a+'");
        yield return Case("simple_match", "name.matches(\"a+\")", TestDialects.MySql, "name REGEXP 'a+'");
        yield return Case("simple_match", "name.matches(\"a+\")", TestDialects.DuckDb, "name ~ 'a+'");
        yield return Case("simple_match", "name.matches(\"a+\")", TestDialects.BigQuery, "REGEXP_CONTAINS(name, 'a+')");

        // word_boundary: PG converts \b to \y, others keep \b
        yield return Case("word_boundary", "name.matches(\"\\\\btest\\\\b\")", TestDialects.PostgreSql, "name ~ '\\ytest\\y'");
        yield return Case("word_boundary", "name.matches(\"\\\\btest\\\\b\")", TestDialects.MySql, "name REGEXP '\\btest\\b'");
        yield return Case("word_boundary", "name.matches(\"\\\\btest\\\\b\")", TestDialects.DuckDb, "name ~ '\\btest\\b'");
        yield return Case("word_boundary", "name.matches(\"\\\\btest\\\\b\")", TestDialects.BigQuery, "REGEXP_CONTAINS(name, '\\btest\\b')");

        // digit_class: PG converts \d to [[:digit:]], others keep \d
        yield return Case("digit_class", "name.matches(\"\\\\d{3}-\\\\d{4}\")", TestDialects.PostgreSql, "name ~ '[[:digit:]]{3}-[[:digit:]]{4}'");
        yield return Case("digit_class", "name.matches(\"\\\\d{3}-\\\\d{4}\")", TestDialects.MySql, "name REGEXP '\\d{3}-\\d{4}'");
        yield return Case("digit_class", "name.matches(\"\\\\d{3}-\\\\d{4}\")", TestDialects.DuckDb, "name ~ '\\d{3}-\\d{4}'");
        yield return Case("digit_class", "name.matches(\"\\\\d{3}-\\\\d{4}\")", TestDialects.BigQuery, "REGEXP_CONTAINS(name, '\\d{3}-\\d{4}')");
    }

    private static object[] Case(string name, string cel, string dialectName, string sql) =>
        new object[] { name, cel, dialectName, sql };

    [Theory]
    [MemberData(nameof(RegexCases))]
    public void Regex(string name, string celExpr, string dialectName, string expectedSql)
    {
        var ast = CelTestEnv.Compile(celExpr);
        var sql = Cel2SqlConverter.Convert(ast, o => o.WithDialect(TestDialects.Get(dialectName)));
        sql.Should().Be(expectedSql, "{0}: CEL '{1}' [{2}]", name, celExpr, dialectName);
    }
}
