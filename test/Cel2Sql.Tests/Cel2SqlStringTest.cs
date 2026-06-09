using FluentAssertions;
using Xunit;

namespace Cel2Sql.Tests;

/// <summary>
/// String function tests covering startsWith, endsWith, contains, and size
/// operations. Mirrors Cel2SqlStringTest.java (PostgreSQL rows only).
/// </summary>
public class Cel2SqlStringTest
{
    public static IEnumerable<object[]> StringCases()
    {
        yield return Case("starts_with", "name.startsWith(\"a\")", "name LIKE 'a%' ESCAPE E'\\\\'");
        yield return Case("ends_with", "name.endsWith(\"z\")", "name LIKE '%z' ESCAPE E'\\\\'");
        yield return Case("contains", "name.contains(\"abc\")", "POSITION('abc' IN name) > 0");
        yield return Case("size_string", "size(\"test\")", "LENGTH('test')");
        yield return Case("size_string_var", "name.size()", "LENGTH(name)");
        yield return Case("size_string_global", "size(name)", "LENGTH(name)");
    }

    private static object[] Case(string name, string cel, string sql) =>
        new object[] { name, cel, TestDialects.PostgreSql, sql };

    [Theory]
    [MemberData(nameof(StringCases))]
    public void String(string name, string celExpr, string dialectName, string expectedSql)
    {
        var ast = CelTestEnv.Compile(celExpr);
        var sql = Cel2SqlConverter.Convert(ast, o => o.WithDialect(TestDialects.Get(dialectName)));
        sql.Should().Be(expectedSql, "{0}: CEL '{1}' [{2}]", name, celExpr, dialectName);
    }
}
