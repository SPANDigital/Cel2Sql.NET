using Cel2Sql.Dialects;
using Cel2Sql.Dialects.BigQuery;
using Cel2Sql.Dialects.DuckDb;
using Cel2Sql.Dialects.MySql;
using Cel2Sql.Dialects.Postgres;
using Cel2Sql.Dialects.Spark;
using Cel2Sql.Dialects.Sqlite;

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
        [MySql] = new MySqlDialect(),
        [Sqlite] = new SqliteDialect(),
        [DuckDb] = new DuckDbDialect(),
        [BigQuery] = new BigQueryDialect(),
        [Spark] = new SparkDialect(),
    };

    /// <summary>The five dialects that share identical SQL for the "uniform" basic/operator cases.</summary>
    public static readonly string[] FiveSqlDialects = { PostgreSql, MySql, Sqlite, DuckDb, BigQuery };

    /// <summary>All implemented dialect names.</summary>
    public static readonly string[] Available = { PostgreSql, MySql, Sqlite, DuckDb, BigQuery, Spark };

    public static IDialect Get(string name) => ByName[name];
}
