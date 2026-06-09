using FluentAssertions;
using Xunit;

namespace Cel2Sql.Tests;

/// <summary>
/// Timestamp and duration tests covering duration parsing and timestamp
/// component extraction. Mirrors Cel2SqlTimestampTest.java (PostgreSQL rows only).
/// </summary>
public class Cel2SqlTimestampTest
{
    public static IEnumerable<object[]> TimestampCases()
    {
        yield return Case("duration_second", "duration(\"10s\")", "INTERVAL 10 SECOND");
        yield return Case("duration_minute", "duration(\"1h1m\")", "INTERVAL 61 MINUTE");
        yield return Case("duration_hour", "duration(\"60m\")", "INTERVAL 1 HOUR");
        yield return Case("timestamp_getSeconds", "created_at.getSeconds()", "EXTRACT(SECOND FROM created_at)");
        yield return Case("timestamp_getMinutes", "created_at.getMinutes()", "EXTRACT(MINUTE FROM created_at)");
        yield return Case("timestamp_getHours", "created_at.getHours()", "EXTRACT(HOUR FROM created_at)");
        yield return Case("timestamp_getFullYear", "created_at.getFullYear()", "EXTRACT(YEAR FROM created_at)");
        yield return Case("timestamp_getMonth", "created_at.getMonth()", "EXTRACT(MONTH FROM created_at)");
        yield return Case("timestamp_getDayOfMonth", "created_at.getDayOfMonth()", "EXTRACT(DAY FROM created_at)");
        yield return Case("timestamp_getDayOfWeek", "created_at.getDayOfWeek()", "(EXTRACT(DOW FROM created_at) + 6) % 7");
        yield return Case("timestamp_getDayOfYear", "created_at.getDayOfYear()", "EXTRACT(DOY FROM created_at)");
    }

    private static object[] Case(string name, string cel, string sql) =>
        new object[] { name, cel, TestDialects.PostgreSql, sql };

    [Theory]
    [MemberData(nameof(TimestampCases))]
    public void Timestamp(string name, string celExpr, string dialectName, string expectedSql)
    {
        var ast = CelTestEnv.Compile(celExpr);
        var sql = Cel2SqlConverter.Convert(ast, o => o.WithDialect(TestDialects.Get(dialectName)));
        sql.Should().Be(expectedSql, "{0}: CEL '{1}' [{2}]", name, celExpr, dialectName);
    }
}
