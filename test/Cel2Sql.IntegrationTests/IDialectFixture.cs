using System.Data.Common;
using Cel2Sql.Dialects;
using Xunit;

namespace Cel2Sql.IntegrationTests;

/// <summary>
/// A per-dialect fixture: owns an open database connection (seeded with the shared
/// <c>test_data</c> table) and the <see cref="IDialect"/> under test. One instance is created
/// per test class via <see cref="IClassFixture{TFixture}"/>.
/// </summary>
public interface IDialectFixture : IAsyncLifetime
{
    /// <summary>The dialect whose generated SQL is being executed.</summary>
    IDialect Dialect { get; }

    /// <summary>The open, seeded connection.</summary>
    DbConnection Connection { get; }

    /// <summary>
    /// Builds a command for parameterized execution. The incoming <paramref name="sql"/> uses the
    /// dialect's native placeholder style ($N / ? / @pN); each provider rewrites/binds as needed.
    /// </summary>
    DbCommand BuildParamCommand(string sql, IReadOnlyList<object?> parameters);
}
