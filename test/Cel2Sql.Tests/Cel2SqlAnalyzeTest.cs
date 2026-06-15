using Cel2Sql.Dialects.BigQuery;
using Cel2Sql.Dialects.DuckDb;
using Cel2Sql.Dialects.MySql;
using Cel2Sql.Dialects.Postgres;
using Cel2Sql.Dialects.Spark;
using Cel2Sql.Dialects.Sqlite;
using AwesomeAssertions;
using Xunit;

namespace Cel2Sql.Tests;

/// <summary>
/// Tests for the query analysis and index recommendation feature.
/// Mirrors Cel2SqlAnalyzeTest.java.
/// </summary>
public class Cel2SqlAnalyzeTest
{
    [Fact]
    public void PostgresComparisonIndex()
    {
        var ast = CelTestEnv.Compile("age > 20");
        var result = Cel2SqlConverter.AnalyzeQuery(ast, o => o.WithDialect(new PostgresDialect()));

        result.Sql.Should().Be("age > 20");
        result.Recommendations.Should().HaveCount(1);

        var rec = result.Recommendations[0];
        rec.Column.Should().Be("age");
        rec.IndexType.Should().Be("BTREE");
        rec.Expression.Should().Contain("CREATE INDEX");
    }

    [Fact]
    public void PostgresRegexIndex()
    {
        var ast = CelTestEnv.Compile("name.matches(\"a+\")");
        var result = Cel2SqlConverter.AnalyzeQuery(ast, o => o.WithDialect(new PostgresDialect()));

        result.Recommendations.Should().HaveCount(1);
        var rec = result.Recommendations[0];
        rec.Column.Should().Be("name");
        rec.IndexType.Should().Be("GIN");
        rec.Expression.Should().Contain("gin_trgm_ops");
    }

    [Fact]
    public void MysqlComparisonIndex()
    {
        var ast = CelTestEnv.Compile("age == 25");
        var result = Cel2SqlConverter.AnalyzeQuery(ast, o => o.WithDialect(new MySqlDialect()));

        result.Recommendations.Should().HaveCount(1);
        var rec = result.Recommendations[0];
        rec.IndexType.Should().Be("BTREE");
    }

    [Fact]
    public void DuckdbComparisonIndex()
    {
        var ast = CelTestEnv.Compile("age < 30");
        var result = Cel2SqlConverter.AnalyzeQuery(ast, o => o.WithDialect(new DuckDbDialect()));

        result.Recommendations.Should().HaveCount(1);
        var rec = result.Recommendations[0];
        rec.IndexType.Should().Be("ART");
    }

    [Fact]
    public void BigqueryComparisonIndex()
    {
        var ast = CelTestEnv.Compile("age >= 18");
        var result = Cel2SqlConverter.AnalyzeQuery(ast, o => o.WithDialect(new BigQueryDialect()));

        result.Recommendations.Should().HaveCount(1);
        var rec = result.Recommendations[0];
        rec.IndexType.Should().Be("CLUSTERING");
    }

    [Fact]
    public void SqliteComparisonIndex()
    {
        var ast = CelTestEnv.Compile("name == \"test\"");
        var result = Cel2SqlConverter.AnalyzeQuery(ast, o => o.WithDialect(new SqliteDialect()));

        result.Recommendations.Should().HaveCount(1);
        var rec = result.Recommendations[0];
        rec.IndexType.Should().Be("BTREE");
    }

    [Fact]
    public void SparkReturnsEmptyRecommendations()
    {
        // Spark indexing is storage-layer specific; analyzeQuery returns no recommendations.
        var ast = CelTestEnv.Compile("name == \"a\" && age > 20");
        var result = Cel2SqlConverter.AnalyzeQuery(ast, o => o.WithDialect(new SparkDialect()));

        result.Recommendations.Should().BeEmpty();
    }

    [Fact]
    public void MultipleColumns()
    {
        var ast = CelTestEnv.Compile("name == \"a\" && age > 20");
        var result = Cel2SqlConverter.AnalyzeQuery(ast, o => o.WithDialect(new PostgresDialect()));

        result.Sql.Should().Be("name = 'a' AND age > 20");
        result.Recommendations.Should().HaveCount(2);
    }

    [Fact]
    public void NoRecommendationsForLiterals()
    {
        var ast = CelTestEnv.Compile("true");
        var result = Cel2SqlConverter.AnalyzeQuery(ast, o => o.WithDialect(new PostgresDialect()));

        result.Sql.Should().Be("TRUE");
        result.Recommendations.Should().BeEmpty();
    }

    [Fact]
    public void ComprehensionIndex()
    {
        var ast = CelTestEnv.Compile("string_list.all(x, x != \"bad\")");
        var result = Cel2SqlConverter.AnalyzeQuery(ast, o => o.WithDialect(new PostgresDialect()));

        result.Recommendations.Should().Contain(rec =>
            rec.Column == "string_list" && rec.IndexType == "GIN");
    }
}
