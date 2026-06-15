using System.Data;
using System.Data.Common;
using Cel2Sql.Dialects;
using AwesomeAssertions;

namespace Cel2Sql.IntegrationTests;

/// <summary>
/// Shared execution/assertion logic for the dialect integration tests. Ports the Java
/// <c>AbstractDialectIntegrationTest</c> execution helpers. Concrete test classes wire up a
/// fixture and expose <c>[MemberData]</c> filtered for their dialect's capabilities.
/// </summary>
public abstract class DialectIntegrationTestBase
{
    private readonly IDialectFixture _fixture;

    protected DialectIntegrationTestBase(IDialectFixture fixture) => _fixture = fixture;

    private IDialect Dialect => _fixture.Dialect;
    private DbConnection Connection => _fixture.Connection;

    /// <summary>Runs one WHERE / expression-only case from the catalog.</summary>
    protected void RunSqlCase(string name, string cel, int[] expectedRowIds, bool expressionOnly, string category)
    {
        var ast = IntegrationCelEnv.Compile(cel);
        string sql = Cel2SqlConverter.Convert(ast, o => o.WithDialect(Dialect));

        if (expressionOnly)
        {
            AssertExpressionExecutes(sql);
        }
        else
        {
            var actual = ExecuteWhereClause(sql);
            actual.Should().BeEquivalentTo(expectedRowIds,
                $"CEL '{cel}' -> SQL '{sql}'");
        }
    }

    /// <summary>Runs one parameterized WHERE case from the catalog.</summary>
    protected void RunParamCase(string name, string cel, int[] expectedRowIds, bool expressionOnly, string category)
    {
        var ast = IntegrationCelEnv.Compile(cel);
        var result = Cel2SqlConverter.ConvertParameterized(ast, o => o.WithDialect(Dialect));

        var actual = ExecuteParameterizedWhereClause(result.Sql, result.Parameters);
        actual.Should().BeEquivalentTo(expectedRowIds,
            $"CEL '{cel}' -> parameterized SQL '{result.Sql}' params=[{string.Join(", ", result.Parameters)}]");
    }

    private HashSet<int> ExecuteWhereClause(string whereClause)
    {
        string query = "SELECT id FROM test_data WHERE " + whereClause;
        var ids = new HashSet<int>();
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = query;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ids.Add(Convert.ToInt32(reader.GetValue(0)));
        }
        return ids;
    }

    private void AssertExpressionExecutes(string expression)
    {
        string query = "SELECT " + expression;
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = query;
        using var reader = cmd.ExecuteReader();
        reader.Read().Should().BeTrue($"Expression '{expression}' should return a result");
    }

    private HashSet<int> ExecuteParameterizedWhereClause(string whereClause, IReadOnlyList<object?> parameters)
    {
        string query = "SELECT id FROM test_data WHERE " + whereClause;
        var ids = new HashSet<int>();
        using var cmd = _fixture.BuildParamCommand(query, parameters);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ids.Add(Convert.ToInt32(reader.GetValue(0)));
        }
        return ids;
    }
}
