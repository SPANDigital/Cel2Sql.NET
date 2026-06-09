using System.Data.Common;
using Cel2Sql.Dialects;
using Cel2Sql.Dialects.Sqlite;
using Microsoft.Data.Sqlite;

namespace Cel2Sql.IntegrationTests.Fixtures;

/// <summary>
/// In-memory SQLite fixture. The same connection is kept open for the fixture lifetime —
/// an in-memory SQLite database is destroyed when its last connection closes.
/// Ports the Java <c>SqliteIntegrationTest</c> DDL/seed.
/// </summary>
public sealed class SqliteFixture : IDialectFixture
{
    private SqliteConnection _connection = null!;

    public IDialect Dialect { get; } = new SqliteDialect();

    public DbConnection Connection => _connection;

    public Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        Exec(@"
            CREATE TABLE test_data (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                age INTEGER NOT NULL,
                adult INTEGER NOT NULL,
                height REAL NOT NULL,
                active INTEGER NOT NULL,
                null_var TEXT,
                string_list TEXT NOT NULL,
                int_list TEXT NOT NULL,
                created_at TEXT NOT NULL
            )");

        Exec(@"
            INSERT INTO test_data VALUES
            (1, 'Alice',    30, 1, 1.65,         1, 'hello', '[""good"",""bad"",""ok""]',      '[1,2,3]',   '2024-06-15T10:30:00'),
            (2, 'Bob',      17, 0, 1.80,         0, NULL,    '[""good"",""great""]',          '[4,5]',     '2024-01-01T00:00:00'),
            (3, 'Carol',    25, 1, 1.70,         1, 'world', '[""bad""]',                   '[6]',       '2024-03-20T15:45:30'),
            (4, 'Dave',     45, 1, 1.90,         0, NULL,    '[""unique"",""good""]',          '[7,8,9]',   '2023-12-25T08:00:00'),
            (5, 'Eve',      20, 1, 1.6180339887, 1, '',      '[""good"",""bad"",""unique""]',    '[10]',      '2024-07-04T12:00:00'),
            (6, 'aardvark', 10, 0, 1.40,         0, NULL,    '[]',                         '[]',        '2024-02-29T23:59:59')");

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _connection?.Dispose();
        return Task.CompletedTask;
    }

    public DbCommand BuildParamCommand(string sql, IReadOnlyList<object?> parameters)
    {
        // Microsoft.Data.Sqlite does not support positional '?'. Rewrite the sequential
        // placeholders to named $p0, $p1, ... and bind by name.
        int idx = 0;
        string rewritten = ReplaceSequential(sql, () => "$p" + idx++);

        var cmd = _connection.CreateCommand();
        cmd.CommandText = rewritten;
        for (int i = 0; i < parameters.Count; i++)
        {
            cmd.Parameters.Add(new SqliteParameter("$p" + i, ToDbValue(parameters[i])));
        }
        return cmd;
    }

    private void Exec(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Replaces each sequential '?' placeholder with the result of <paramref name="next"/>.</summary>
    internal static string ReplaceSequential(string sql, Func<string> next)
    {
        var sb = new System.Text.StringBuilder(sql.Length);
        foreach (char c in sql)
        {
            if (c == '?') sb.Append(next());
            else sb.Append(c);
        }
        return sb.ToString();
    }

    internal static object ToDbValue(object? v) => v ?? DBNull.Value;
}
