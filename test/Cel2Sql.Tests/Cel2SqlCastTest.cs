using AwesomeAssertions;
using Xunit;

namespace Cel2Sql.Tests;

/// <summary>
/// Type cast tests covering int(), string(), double() casts and int(timestamp)
/// epoch extraction across all 5 SQL dialects. Mirrors Cel2SqlCastTest.java.
/// </summary>
public class Cel2SqlCastTest
{
    public static IEnumerable<object[]> CastCases()
    {
        // cast_int_from_string: int type name varies per dialect
        yield return Case("cast_int_from_string", "int(\"42\") == 42", TestDialects.PostgreSql, "CAST('42' AS BIGINT) = 42");
        yield return Case("cast_int_from_string", "int(\"42\") == 42", TestDialects.MySql, "CAST('42' AS SIGNED) = 42");
        yield return Case("cast_int_from_string", "int(\"42\") == 42", TestDialects.Sqlite, "CAST('42' AS INTEGER) = 42");
        yield return Case("cast_int_from_string", "int(\"42\") == 42", TestDialects.DuckDb, "CAST('42' AS BIGINT) = 42");
        yield return Case("cast_int_from_string", "int(\"42\") == 42", TestDialects.BigQuery, "CAST('42' AS INT64) = 42");

        // cast_string_from_int: string type name varies per dialect
        yield return Case("cast_string_from_int", "string(42) == \"42\"", TestDialects.PostgreSql, "CAST(42 AS TEXT) = '42'");
        yield return Case("cast_string_from_int", "string(42) == \"42\"", TestDialects.MySql, "CAST(42 AS CHAR) = '42'");
        yield return Case("cast_string_from_int", "string(42) == \"42\"", TestDialects.Sqlite, "CAST(42 AS TEXT) = '42'");
        yield return Case("cast_string_from_int", "string(42) == \"42\"", TestDialects.DuckDb, "CAST(42 AS VARCHAR) = '42'");
        yield return Case("cast_string_from_int", "string(42) == \"42\"", TestDialects.BigQuery, "CAST(42 AS STRING) = '42'");

        // cast_int_from_double: int type name varies per dialect
        yield return Case("cast_int_from_double", "int(height)", TestDialects.PostgreSql, "CAST(height AS BIGINT)");
        yield return Case("cast_int_from_double", "int(height)", TestDialects.MySql, "CAST(height AS SIGNED)");
        yield return Case("cast_int_from_double", "int(height)", TestDialects.Sqlite, "CAST(height AS INTEGER)");
        yield return Case("cast_int_from_double", "int(height)", TestDialects.DuckDb, "CAST(height AS BIGINT)");
        yield return Case("cast_int_from_double", "int(height)", TestDialects.BigQuery, "CAST(height AS INT64)");

        // cast_double_from_int: double type name varies per dialect
        yield return Case("cast_double_from_int", "double(age)", TestDialects.PostgreSql, "CAST(age AS DOUBLE PRECISION)");
        yield return Case("cast_double_from_int", "double(age)", TestDialects.MySql, "CAST(age AS DECIMAL)");
        yield return Case("cast_double_from_int", "double(age)", TestDialects.Sqlite, "CAST(age AS REAL)");
        yield return Case("cast_double_from_int", "double(age)", TestDialects.DuckDb, "CAST(age AS DOUBLE)");
        yield return Case("cast_double_from_int", "double(age)", TestDialects.BigQuery, "CAST(age AS FLOAT64)");

        // cast_string_from_var: string type name varies per dialect
        yield return Case("cast_string_from_var", "string(age)", TestDialects.PostgreSql, "CAST(age AS TEXT)");
        yield return Case("cast_string_from_var", "string(age)", TestDialects.MySql, "CAST(age AS CHAR)");
        yield return Case("cast_string_from_var", "string(age)", TestDialects.Sqlite, "CAST(age AS TEXT)");
        yield return Case("cast_string_from_var", "string(age)", TestDialects.DuckDb, "CAST(age AS VARCHAR)");
        yield return Case("cast_string_from_var", "string(age)", TestDialects.BigQuery, "CAST(age AS STRING)");

        // cast_int_epoch: special epoch extraction per dialect
        yield return Case("cast_int_epoch", "int(created_at)", TestDialects.PostgreSql, "EXTRACT(EPOCH FROM created_at)::bigint");
        yield return Case("cast_int_epoch", "int(created_at)", TestDialects.MySql, "UNIX_TIMESTAMP(created_at)");
        yield return Case("cast_int_epoch", "int(created_at)", TestDialects.Sqlite, "CAST(strftime('%s', created_at) AS INTEGER)");
        yield return Case("cast_int_epoch", "int(created_at)", TestDialects.DuckDb, "EXTRACT(EPOCH FROM created_at)::BIGINT");
        yield return Case("cast_int_epoch", "int(created_at)", TestDialects.BigQuery, "UNIX_SECONDS(created_at)");
    }

    private static object[] Case(string name, string cel, string dialectName, string sql) =>
        new object[] { name, cel, dialectName, sql };

    [Theory]
    [MemberData(nameof(CastCases))]
    public void Cast(string name, string celExpr, string dialectName, string expectedSql)
    {
        var ast = CelTestEnv.Compile(celExpr);
        var sql = Cel2SqlConverter.Convert(ast, o => o.WithDialect(TestDialects.Get(dialectName)));
        sql.Should().Be(expectedSql, "{0}: CEL '{1}' [{2}]", name, celExpr, dialectName);
    }
}
