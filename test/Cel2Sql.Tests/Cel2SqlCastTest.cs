using FluentAssertions;
using Xunit;

namespace Cel2Sql.Tests;

/// <summary>
/// Type cast tests covering int(), string(), double() casts and int(timestamp)
/// epoch extraction. Mirrors Cel2SqlCastTest.java (PostgreSQL rows only).
/// </summary>
public class Cel2SqlCastTest
{
    public static IEnumerable<object[]> CastCases()
    {
        yield return Case("cast_int_from_string", "int(\"42\") == 42", "CAST('42' AS BIGINT) = 42");
        yield return Case("cast_string_from_int", "string(42) == \"42\"", "CAST(42 AS TEXT) = '42'");
        yield return Case("cast_int_from_double", "int(height)", "CAST(height AS BIGINT)");
        yield return Case("cast_double_from_int", "double(age)", "CAST(age AS DOUBLE PRECISION)");
        yield return Case("cast_string_from_var", "string(age)", "CAST(age AS TEXT)");
        yield return Case("cast_int_epoch", "int(created_at)", "EXTRACT(EPOCH FROM created_at)::bigint");
    }

    private static object[] Case(string name, string cel, string sql) =>
        new object[] { name, cel, TestDialects.PostgreSql, sql };

    [Theory]
    [MemberData(nameof(CastCases))]
    public void Cast(string name, string celExpr, string dialectName, string expectedSql)
    {
        var ast = CelTestEnv.Compile(celExpr);
        var sql = Cel2SqlConverter.Convert(ast, o => o.WithDialect(TestDialects.Get(dialectName)));
        sql.Should().Be(expectedSql, "{0}: CEL '{1}' [{2}]", name, celExpr, dialectName);
    }
}
