using FluentAssertions;
using Xunit;

namespace Cel2Sql.Tests;

/// <summary>
/// Timestamp and duration tests covering duration parsing, timestamp
/// component extraction, and timestamp arithmetic across all 5 dialects.
/// Mirrors Cel2SqlTimestampTest.java.
/// </summary>
public class Cel2SqlTimestampTest
{
    public static IEnumerable<object[]> TimestampCases()
    {
        // duration_second: PG/MySQL/DuckDB/BQ use INTERVAL, SQLite uses string
        yield return Case("duration_second", "duration(\"10s\")", TestDialects.PostgreSql, "INTERVAL 10 SECOND");
        yield return Case("duration_second", "duration(\"10s\")", TestDialects.MySql, "INTERVAL 10 SECOND");
        yield return Case("duration_second", "duration(\"10s\")", TestDialects.Sqlite, "'+10 seconds'");
        yield return Case("duration_second", "duration(\"10s\")", TestDialects.DuckDb, "INTERVAL 10 SECOND");
        yield return Case("duration_second", "duration(\"10s\")", TestDialects.BigQuery, "INTERVAL 10 SECOND");

        yield return Case("duration_minute", "duration(\"1h1m\")", TestDialects.PostgreSql, "INTERVAL 61 MINUTE");
        yield return Case("duration_minute", "duration(\"1h1m\")", TestDialects.MySql, "INTERVAL 61 MINUTE");
        yield return Case("duration_minute", "duration(\"1h1m\")", TestDialects.Sqlite, "'+61 minutes'");
        yield return Case("duration_minute", "duration(\"1h1m\")", TestDialects.DuckDb, "INTERVAL 61 MINUTE");
        yield return Case("duration_minute", "duration(\"1h1m\")", TestDialects.BigQuery, "INTERVAL 61 MINUTE");

        yield return Case("duration_hour", "duration(\"60m\")", TestDialects.PostgreSql, "INTERVAL 1 HOUR");
        yield return Case("duration_hour", "duration(\"60m\")", TestDialects.MySql, "INTERVAL 1 HOUR");
        yield return Case("duration_hour", "duration(\"60m\")", TestDialects.Sqlite, "'+1 hours'");
        yield return Case("duration_hour", "duration(\"60m\")", TestDialects.DuckDb, "INTERVAL 1 HOUR");
        yield return Case("duration_hour", "duration(\"60m\")", TestDialects.BigQuery, "INTERVAL 1 HOUR");

        // getSeconds: EXTRACT vs strftime
        yield return Case("timestamp_getSeconds", "created_at.getSeconds()", TestDialects.PostgreSql, "EXTRACT(SECOND FROM created_at)");
        yield return Case("timestamp_getSeconds", "created_at.getSeconds()", TestDialects.MySql, "EXTRACT(SECOND FROM created_at)");
        yield return Case("timestamp_getSeconds", "created_at.getSeconds()", TestDialects.Sqlite, "CAST(strftime('%S', created_at) AS INTEGER)");
        yield return Case("timestamp_getSeconds", "created_at.getSeconds()", TestDialects.DuckDb, "EXTRACT(SECOND FROM created_at)");
        yield return Case("timestamp_getSeconds", "created_at.getSeconds()", TestDialects.BigQuery, "EXTRACT(SECOND FROM created_at)");

        yield return Case("timestamp_getMinutes", "created_at.getMinutes()", TestDialects.PostgreSql, "EXTRACT(MINUTE FROM created_at)");
        yield return Case("timestamp_getMinutes", "created_at.getMinutes()", TestDialects.MySql, "EXTRACT(MINUTE FROM created_at)");
        yield return Case("timestamp_getMinutes", "created_at.getMinutes()", TestDialects.Sqlite, "CAST(strftime('%M', created_at) AS INTEGER)");
        yield return Case("timestamp_getMinutes", "created_at.getMinutes()", TestDialects.DuckDb, "EXTRACT(MINUTE FROM created_at)");
        yield return Case("timestamp_getMinutes", "created_at.getMinutes()", TestDialects.BigQuery, "EXTRACT(MINUTE FROM created_at)");

        yield return Case("timestamp_getHours", "created_at.getHours()", TestDialects.PostgreSql, "EXTRACT(HOUR FROM created_at)");
        yield return Case("timestamp_getHours", "created_at.getHours()", TestDialects.MySql, "EXTRACT(HOUR FROM created_at)");
        yield return Case("timestamp_getHours", "created_at.getHours()", TestDialects.Sqlite, "CAST(strftime('%H', created_at) AS INTEGER)");
        yield return Case("timestamp_getHours", "created_at.getHours()", TestDialects.DuckDb, "EXTRACT(HOUR FROM created_at)");
        yield return Case("timestamp_getHours", "created_at.getHours()", TestDialects.BigQuery, "EXTRACT(HOUR FROM created_at)");

        yield return Case("timestamp_getFullYear", "created_at.getFullYear()", TestDialects.PostgreSql, "EXTRACT(YEAR FROM created_at)");
        yield return Case("timestamp_getFullYear", "created_at.getFullYear()", TestDialects.MySql, "EXTRACT(YEAR FROM created_at)");
        yield return Case("timestamp_getFullYear", "created_at.getFullYear()", TestDialects.Sqlite, "CAST(strftime('%Y', created_at) AS INTEGER)");
        yield return Case("timestamp_getFullYear", "created_at.getFullYear()", TestDialects.DuckDb, "EXTRACT(YEAR FROM created_at)");
        yield return Case("timestamp_getFullYear", "created_at.getFullYear()", TestDialects.BigQuery, "EXTRACT(YEAR FROM created_at)");

        yield return Case("timestamp_getMonth", "created_at.getMonth()", TestDialects.PostgreSql, "EXTRACT(MONTH FROM created_at)");
        yield return Case("timestamp_getMonth", "created_at.getMonth()", TestDialects.MySql, "EXTRACT(MONTH FROM created_at)");
        yield return Case("timestamp_getMonth", "created_at.getMonth()", TestDialects.Sqlite, "CAST(strftime('%m', created_at) AS INTEGER)");
        yield return Case("timestamp_getMonth", "created_at.getMonth()", TestDialects.DuckDb, "EXTRACT(MONTH FROM created_at)");
        yield return Case("timestamp_getMonth", "created_at.getMonth()", TestDialects.BigQuery, "EXTRACT(MONTH FROM created_at)");

        yield return Case("timestamp_getDayOfMonth", "created_at.getDayOfMonth()", TestDialects.PostgreSql, "EXTRACT(DAY FROM created_at)");
        yield return Case("timestamp_getDayOfMonth", "created_at.getDayOfMonth()", TestDialects.MySql, "EXTRACT(DAY FROM created_at)");
        yield return Case("timestamp_getDayOfMonth", "created_at.getDayOfMonth()", TestDialects.Sqlite, "CAST(strftime('%d', created_at) AS INTEGER)");
        yield return Case("timestamp_getDayOfMonth", "created_at.getDayOfMonth()", TestDialects.DuckDb, "EXTRACT(DAY FROM created_at)");
        yield return Case("timestamp_getDayOfMonth", "created_at.getDayOfMonth()", TestDialects.BigQuery, "EXTRACT(DAY FROM created_at)");

        // getDayOfWeek: special handling per dialect
        yield return Case("timestamp_getDayOfWeek", "created_at.getDayOfWeek()", TestDialects.PostgreSql, "(EXTRACT(DOW FROM created_at) + 6) % 7");
        yield return Case("timestamp_getDayOfWeek", "created_at.getDayOfWeek()", TestDialects.MySql, "(DAYOFWEEK(created_at) + 5) % 7");
        yield return Case("timestamp_getDayOfWeek", "created_at.getDayOfWeek()", TestDialects.Sqlite, "CAST(strftime('%w', created_at) AS INTEGER)");
        yield return Case("timestamp_getDayOfWeek", "created_at.getDayOfWeek()", TestDialects.DuckDb, "(EXTRACT(DOW FROM created_at) + 6) % 7");
        yield return Case("timestamp_getDayOfWeek", "created_at.getDayOfWeek()", TestDialects.BigQuery, "(EXTRACT(DAYOFWEEK FROM created_at) - 1)");

        // getDayOfYear: EXTRACT(DOY) vs strftime
        yield return Case("timestamp_getDayOfYear", "created_at.getDayOfYear()", TestDialects.PostgreSql, "EXTRACT(DOY FROM created_at)");
        yield return Case("timestamp_getDayOfYear", "created_at.getDayOfYear()", TestDialects.MySql, "EXTRACT(DOY FROM created_at)");
        yield return Case("timestamp_getDayOfYear", "created_at.getDayOfYear()", TestDialects.Sqlite, "CAST(strftime('%j', created_at) AS INTEGER)");
        yield return Case("timestamp_getDayOfYear", "created_at.getDayOfYear()", TestDialects.DuckDb, "EXTRACT(DOY FROM created_at)");
        yield return Case("timestamp_getDayOfYear", "created_at.getDayOfYear()", TestDialects.BigQuery, "EXTRACT(DOY FROM created_at)");
    }

    private static object[] Case(string name, string cel, string dialectName, string sql) =>
        new object[] { name, cel, dialectName, sql };

    [Theory]
    [MemberData(nameof(TimestampCases))]
    public void Timestamp(string name, string celExpr, string dialectName, string expectedSql)
    {
        var ast = CelTestEnv.Compile(celExpr);
        var sql = Cel2SqlConverter.Convert(ast, o => o.WithDialect(TestDialects.Get(dialectName)));
        sql.Should().Be(expectedSql, "{0}: CEL '{1}' [{2}]", name, celExpr, dialectName);
    }
}
