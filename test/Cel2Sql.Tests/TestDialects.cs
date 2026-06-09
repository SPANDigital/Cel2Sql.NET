using Cel2Sql.Dialects;
using Cel2Sql.Dialects.Postgres;

namespace Cel2Sql.Tests;

/// <summary>
/// Registry of dialect instances for parameterized tests. Theory data carries the dialect
/// <em>name</em> (a serializable string); the test resolves the instance via <see cref="ByName"/>.
/// </summary>
public static class TestDialects
{
    public const string PostgreSql = "PostgreSQL";
    public const string MySql = "MySQL";
    public const string Sqlite = "SQLite";
    public const string DuckDb = "DuckDB";
    public const string BigQuery = "BigQuery";
    public const string Spark = "Spark";

    public static readonly IReadOnlyDictionary<string, IDialect> ByName = new Dictionary<string, IDialect>
    {
        [PostgreSql] = new PostgresDialect(),
        // M3 adds: MySql, Sqlite, DuckDb, BigQuery, Spark
    };

    /// <summary>Dialect names that currently have an implementation (drives the cross-dialect fan-out).</summary>
    public static readonly string[] Available = { PostgreSql };

    public static IDialect Get(string name) => ByName[name];
}
