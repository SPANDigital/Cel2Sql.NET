using System.Data.Common;
using Cel2Sql.Dialects;
using Cel2Sql.Dialects.Postgres;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Cel2Sql.IntegrationTests.Fixtures;

/// <summary>
/// PostgreSQL fixture backed by a Testcontainers <c>postgres:16-alpine</c> container.
/// Ports the Java <c>PostgresIntegrationTest</c> DDL/seed. Requires a container runtime.
/// </summary>
public sealed class PostgresFixture : IDialectFixture
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("cel2sql_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private NpgsqlConnection _connection = null!;

    public IDialect Dialect { get; } = new PostgresDialect();

    public DbConnection Connection => _connection;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _connection = new NpgsqlConnection(_container.GetConnectionString());
        await _connection.OpenAsync();

        Exec("SET TIMEZONE TO 'UTC'");
        Exec(@"
            CREATE TABLE test_data (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                age BIGINT NOT NULL,
                adult BOOLEAN NOT NULL,
                height DOUBLE PRECISION NOT NULL,
                active BOOLEAN NOT NULL,
                null_var TEXT,
                string_list TEXT[] NOT NULL,
                int_list INTEGER[] NOT NULL,
                created_at TIMESTAMPTZ NOT NULL
            )");
        Exec(@"
            INSERT INTO test_data VALUES
            (1, 'Alice',    30, TRUE,  1.65,         TRUE,  'hello', ARRAY['good','bad','ok'],      ARRAY[1,2,3],   '2024-06-15 10:30:00+00'),
            (2, 'Bob',      17, FALSE, 1.80,         FALSE, NULL,    ARRAY['good','great'],          ARRAY[4,5],     '2024-01-01 00:00:00+00'),
            (3, 'Carol',    25, TRUE,  1.70,         TRUE,  'world', ARRAY['bad'],                   ARRAY[6],       '2024-03-20 15:45:30+00'),
            (4, 'Dave',     45, TRUE,  1.90,         FALSE, NULL,    ARRAY['unique','good'],         ARRAY[7,8,9],   '2023-12-25 08:00:00+00'),
            (5, 'Eve',      20, TRUE,  1.6180339887, TRUE,  '',      ARRAY['good','bad','unique'],   ARRAY[10],      '2024-07-04 12:00:00+00'),
            (6, 'aardvark', 10, FALSE, 1.40,         FALSE, NULL,    ARRAY[]::TEXT[],                ARRAY[]::INT[], '2024-02-29 23:59:59+00')");
    }

    public async Task DisposeAsync()
    {
        if (_connection != null) await _connection.DisposeAsync();
        await _container.DisposeAsync();
    }

    public DbCommand BuildParamCommand(string sql, IReadOnlyList<object?> parameters)
    {
        // Npgsql binds positional $1, $2, ... to unnamed parameters added in order.
        var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parameters)
        {
            cmd.Parameters.Add(new NpgsqlParameter { Value = p ?? DBNull.Value });
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
