using FluentAssertions;
using Xunit;

namespace Cel2Sql.Tests;

/// <summary>
/// Regex matching tests covering simple patterns, word boundary conversion,
/// and character class conversion. Mirrors Cel2SqlRegexTest.java (PostgreSQL rows only).
/// </summary>
public class Cel2SqlRegexTest
{
    public static IEnumerable<object[]> RegexCases()
    {
        yield return Case("simple_match", "name.matches(\"a+\")", "name ~ 'a+'");
        yield return Case("word_boundary", "name.matches(\"\\\\btest\\\\b\")", "name ~ '\\ytest\\y'");
        yield return Case("digit_class", "name.matches(\"\\\\d{3}-\\\\d{4}\")", "name ~ '[[:digit:]]{3}-[[:digit:]]{4}'");
    }

    private static object[] Case(string name, string cel, string sql) =>
        new object[] { name, cel, TestDialects.PostgreSql, sql };

    [Theory]
    [MemberData(nameof(RegexCases))]
    public void Regex(string name, string celExpr, string dialectName, string expectedSql)
    {
        var ast = CelTestEnv.Compile(celExpr);
        var sql = Cel2SqlConverter.Convert(ast, o => o.WithDialect(TestDialects.Get(dialectName)));
        sql.Should().Be(expectedSql, "{0}: CEL '{1}' [{2}]", name, celExpr, dialectName);
    }
}
