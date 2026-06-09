namespace Cel2Sql.Dialects;

/// <summary>Supported SQL dialect names.</summary>
public enum DialectName
{
    PostgreSql,
    MySql,
    Sqlite,
    DuckDb,
    BigQuery,
    Spark,
}

/// <summary>Helpers for <see cref="DialectName"/> string values.</summary>
public static class DialectNameExtensions
{
    /// <summary>Returns the canonical string representation (e.g. "postgresql").</summary>
    public static string Value(this DialectName name) => name switch
    {
        DialectName.PostgreSql => "postgresql",
        DialectName.MySql => "mysql",
        DialectName.Sqlite => "sqlite",
        DialectName.DuckDb => "duckdb",
        DialectName.BigQuery => "bigquery",
        DialectName.Spark => "spark",
        _ => name.ToString().ToLowerInvariant(),
    };
}
