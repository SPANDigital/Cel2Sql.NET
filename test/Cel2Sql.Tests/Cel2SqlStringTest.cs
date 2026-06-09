using FluentAssertions;
using Xunit;

namespace Cel2Sql.Tests;

/// <summary>
/// String function tests covering startsWith, endsWith, contains, and size
/// operations across all 5 SQL dialects. Mirrors Cel2SqlStringTest.java.
/// </summary>
public class Cel2SqlStringTest
{
    public static IEnumerable<object[]> StringCases()
    {
        // startsWith: LIKE escape differs per dialect
        yield return Case("starts_with", "name.startsWith(\"a\")", TestDialects.PostgreSql, "name LIKE 'a%' ESCAPE E'\\\\'");
        yield return Case("starts_with", "name.startsWith(\"a\")", TestDialects.MySql, "name LIKE 'a%' ESCAPE '\\\\'");
        yield return Case("starts_with", "name.startsWith(\"a\")", TestDialects.Sqlite, "name LIKE 'a%' ESCAPE '\\'");
        yield return Case("starts_with", "name.startsWith(\"a\")", TestDialects.DuckDb, "name LIKE 'a%' ESCAPE '\\'");
        yield return Case("starts_with", "name.startsWith(\"a\")", TestDialects.BigQuery, "name LIKE 'a%'");

        // endsWith: LIKE escape differs per dialect
        yield return Case("ends_with", "name.endsWith(\"z\")", TestDialects.PostgreSql, "name LIKE '%z' ESCAPE E'\\\\'");
        yield return Case("ends_with", "name.endsWith(\"z\")", TestDialects.MySql, "name LIKE '%z' ESCAPE '\\\\'");
        yield return Case("ends_with", "name.endsWith(\"z\")", TestDialects.Sqlite, "name LIKE '%z' ESCAPE '\\'");
        yield return Case("ends_with", "name.endsWith(\"z\")", TestDialects.DuckDb, "name LIKE '%z' ESCAPE '\\'");
        yield return Case("ends_with", "name.endsWith(\"z\")", TestDialects.BigQuery, "name LIKE '%z'");

        // contains: function name/syntax differs per dialect
        yield return Case("contains", "name.contains(\"abc\")", TestDialects.PostgreSql, "POSITION('abc' IN name) > 0");
        yield return Case("contains", "name.contains(\"abc\")", TestDialects.MySql, "LOCATE('abc', name) > 0");
        yield return Case("contains", "name.contains(\"abc\")", TestDialects.Sqlite, "INSTR(name, 'abc') > 0");
        yield return Case("contains", "name.contains(\"abc\")", TestDialects.DuckDb, "CONTAINS(name, 'abc')");
        yield return Case("contains", "name.contains(\"abc\")", TestDialects.BigQuery, "STRPOS(name, 'abc') > 0");

        // size/LENGTH: same across all dialects
        foreach (var c in AllDialects("size_string", "size(\"test\")", "LENGTH('test')")) yield return c;
        foreach (var c in AllDialects("size_string_var", "name.size()", "LENGTH(name)")) yield return c;
        foreach (var c in AllDialects("size_string_global", "size(name)", "LENGTH(name)")) yield return c;
    }

    private static IEnumerable<object[]> AllDialects(string name, string cel, string sql)
    {
        foreach (var dialect in TestDialects.FiveSqlDialects)
            yield return Case(name, cel, dialect, sql);
    }

    private static object[] Case(string name, string cel, string dialectName, string sql) =>
        new object[] { name, cel, dialectName, sql };

    [Theory]
    [MemberData(nameof(StringCases))]
    public void String(string name, string celExpr, string dialectName, string expectedSql)
    {
        var ast = CelTestEnv.Compile(celExpr);
        var sql = Cel2SqlConverter.Convert(ast, o => o.WithDialect(TestDialects.Get(dialectName)));
        sql.Should().Be(expectedSql, "{0}: CEL '{1}' [{2}]", name, celExpr, dialectName);
    }
}
