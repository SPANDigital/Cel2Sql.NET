using System.Data.Common;
using Cel2Sql.Dialects;
using Cel2Sql.Dialects.DuckDb;
using DuckDB.NET.Data;

namespace Cel2Sql.IntegrationTests.Fixtures;

/// <summary>
/// In-memory DuckDB fixture. Ports the Java <c>DuckDbIntegrationTest</c> DDL/seed.
/// </summary>
public sealed class DuckDbFixture : IDialectFixture
{
    private DuckDBConnection _connection = null!;

    public IDialect Dialect { get; } = new DuckDbDialect();

    public DbConnection Connection => _connection;

    public Task InitializeAsync()
    {
        _connection = new DuckDBConnection("DataSource=:memory:");
        _connection.Open();

        Exec(@"
            CREATE TABLE test_data (
                id INTEGER PRIMARY KEY,
                name VARCHAR NOT NULL,
                age BIGINT NOT NULL,
                adult BOOLEAN NOT NULL,
                height DOUBLE NOT NULL,
                active BOOLEAN NOT NULL,
                null_var VARCHAR,
                string_list VARCHAR[] NOT NULL,
                int_list INTEGER[] NOT NULL,
                created_at TIMESTAMP NOT NULL
            )");

        Exec("INSERT INTO test_data VALUES (1, 'Alice',    30, TRUE,  1.65,         TRUE,  'hello', ['good','bad','ok'],      [1,2,3],   TIMESTAMP '2024-06-15 10:30:00')");
        Exec("INSERT INTO test_data VALUES (2, 'Bob',      17, FALSE, 1.80,         FALSE, NULL,    ['good','great'],          [4,5],     TIMESTAMP '2024-01-01 00:00:00')");
        Exec("INSERT INTO test_data VALUES (3, 'Carol',    25, TRUE,  1.70,         TRUE,  'world', ['bad'],                   [6],       TIMESTAMP '2024-03-20 15:45:30')");
        Exec("INSERT INTO test_data VALUES (4, 'Dave',     45, TRUE,  1.90,         FALSE, NULL,    ['unique','good'],         [7,8,9],   TIMESTAMP '2023-12-25 08:00:00')");
        Exec("INSERT INTO test_data VALUES (5, 'Eve',      20, TRUE,  1.6180339887, TRUE,  '',      ['good','bad','unique'],   [10],      TIMESTAMP '2024-07-04 12:00:00')");
        Exec("INSERT INTO test_data VALUES (6, 'aardvark', 10, FALSE, 1.40,         FALSE, NULL,    [],                        [],        TIMESTAMP '2024-02-29 23:59:59')");

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _connection?.Dispose();
        return Task.CompletedTask;
    }

    public DbCommand BuildParamCommand(string sql, IReadOnlyList<object?> parameters)
    {
        // DuckDB.NET binds positional parameters in the order they are added. The generated SQL
        // uses $1, $2, ... — DuckDB.NET maps positional parameters to those by ordinal.
        var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parameters)
        {
            var param = new DuckDBParameter { Value = p ?? DBNull.Value };
            cmd.Parameters.Add(param);
        }
        return cmd;
    }

    private void Exec(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
