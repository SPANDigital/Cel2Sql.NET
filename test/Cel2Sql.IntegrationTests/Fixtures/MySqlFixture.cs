using System.Data.Common;
using Cel2Sql.Dialects;
using Cel2Sql.Dialects.MySql;
using MySqlConnector;
using Testcontainers.MySql;

namespace Cel2Sql.IntegrationTests.Fixtures;

/// <summary>
/// MySQL fixture backed by a Testcontainers <c>mysql:8.0</c> container.
/// Ports the Java <c>MySqlIntegrationTest</c> DDL/seed. Requires a container runtime.
/// </summary>
public sealed class MySqlFixture : IDialectFixture
{
    private readonly MySqlContainer _container = new MySqlBuilder()
        .WithImage("mysql:8.0")
        .WithDatabase("cel2sql_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private MySqlConnection _connection = null!;

    public IDialect Dialect { get; } = new MySqlDialect();

    public DbConnection Connection => _connection;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _connection = new MySqlConnection(_container.GetConnectionString());
        await _connection.OpenAsync();

        Exec(@"
            CREATE TABLE test_data (
                id INTEGER PRIMARY KEY,
                name VARCHAR(100) NOT NULL,
                age BIGINT NOT NULL,
                adult BOOLEAN NOT NULL,
                height DOUBLE NOT NULL,
                active BOOLEAN NOT NULL,
                null_var VARCHAR(100),
                string_list JSON NOT NULL,
                int_list JSON NOT NULL,
                created_at DATETIME NOT NULL
            )");
        Exec(@"
            INSERT INTO test_data VALUES
            (1, 'Alice',    30, TRUE,  1.65,         TRUE,  'hello', '[""good"",""bad"",""ok""]',      '[1,2,3]',   '2024-06-15 10:30:00'),
            (2, 'Bob',      17, FALSE, 1.80,         FALSE, NULL,    '[""good"",""great""]',          '[4,5]',     '2024-01-01 00:00:00'),
            (3, 'Carol',    25, TRUE,  1.70,         TRUE,  'world', '[""bad""]',                   '[6]',       '2024-03-20 15:45:30'),
            (4, 'Dave',     45, TRUE,  1.90,         FALSE, NULL,    '[""unique"",""good""]',          '[7,8,9]',   '2023-12-25 08:00:00'),
            (5, 'Eve',      20, TRUE,  1.6180339887, TRUE,  '',      '[""good"",""bad"",""unique""]',    '[10]',      '2024-07-04 12:00:00'),
            (6, 'aardvark', 10, FALSE, 1.40,         FALSE, NULL,    '[]',                         '[]',        '2024-02-29 23:59:59')");
    }

    public async Task DisposeAsync()
    {
        if (_connection != null) await _connection.DisposeAsync();
        await _container.DisposeAsync();
    }

    public DbCommand BuildParamCommand(string sql, IReadOnlyList<object?> parameters)
    {
        // MySqlConnector supports positional '?' placeholders bound by add order.
        var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parameters)
        {
            cmd.Parameters.Add(new MySqlParameter { Value = p ?? DBNull.Value });
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
