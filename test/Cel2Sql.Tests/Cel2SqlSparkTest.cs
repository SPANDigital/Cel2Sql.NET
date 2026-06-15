using System.Text;
using Cel2Sql.Dialects;
using Cel2Sql.Dialects.Spark;
using Cel2Sql.Errors;
using AwesomeAssertions;
using Xunit;

namespace Cel2Sql.Tests;

/// <summary>
/// Apache Spark SQL dialect tests. Test cases covered: RLIKE for regex,
/// <c>array_contains</c> for membership, <c>size</c> + <c>COALESCE</c> for length,
/// and the <c>dayofweek - 1</c> adjustment for <c>getDayOfWeek</c>.
/// Mirrors Cel2SqlSparkTest.java.
/// </summary>
public class Cel2SqlSparkTest
{
    private static IDialect Spark => TestDialects.Get(TestDialects.Spark);

    public static IEnumerable<object[]> SparkBasicTests()
    {
        // Comparisons + boolean/null inlining behave the same as other dialects.
        yield return new object[] { "string_eq", "name == \"Alice\"", "name = 'Alice'" };
        yield return new object[] { "int_gt", "age > 21", "age > 21" };
        yield return new object[] { "bool_true", "active == true", "active IS TRUE" };
        yield return new object[] { "null_check", "null_var == null", "null_var IS NULL" };
        yield return new object[] { "and_logic", "age > 18 && active", "age > 18 AND active" };
        yield return new object[] { "or_logic", "name == \"a\" || name == \"b\"", "name = 'a' OR name = 'b'" };

        // String functions: concat() / RLIKE / LOCATE / LIKE.
        yield return new object[] { "concat", "name + \"_x\"", "concat(name, '_x')" };
        yield return new object[] { "contains", "name.contains(\"li\")", "LOCATE('li', name) > 0" };
        yield return new object[] { "starts_with", "name.startsWith(\"a\")", "name LIKE 'a%' ESCAPE '\\\\'" };
        yield return new object[] { "ends_with", "name.endsWith(\"e\")", "name LIKE '%e' ESCAPE '\\\\'" };
        yield return new object[] { "regex_match", "name.matches(\"^a.*z$\")", "name RLIKE '^a.*z$'" };

        // Arrays: array literal / array_contains / COALESCE(size).
        yield return new object[] { "array_literal", "[1, 2, 3][0] == 1", "array(1, 2, 3)[0] = 1" };
        yield return new object[] { "array_membership", "\"x\" in tags", "array_contains(tags, 'x')" };
        yield return new object[] { "array_size", "size(string_list)", "COALESCE(size(string_list), 0)" };
        yield return new object[] { "array_size_method", "string_list.size()", "COALESCE(size(string_list), 0)" };

        // Timestamps: EXTRACT(... FROM ts), with the dayofweek -1 adjustment.
        yield return new object[] { "year", "created_at.getFullYear()", "EXTRACT(YEAR FROM created_at)" };
        yield return new object[] { "hour", "created_at.getHours()", "EXTRACT(HOUR FROM created_at)" };
        yield return new object[] { "day_of_week", "created_at.getDayOfWeek()", "(dayofweek(created_at) - 1)" };
    }

    [Theory]
    [MemberData(nameof(SparkBasicTests))]
    public void TestSparkBasic(string name, string celExpr, string expectedSql)
    {
        var ast = CelTestEnv.Compile(celExpr);
        var sql = Cel2SqlConverter.Convert(ast, o => o.WithDialect(Spark));
        sql.Should().Be(expectedSql, "{0}: CEL '{1}'", name, celExpr);
    }

    [Fact]
    public void ParameterizedUsesPositionalQuestionMark()
    {
        var ast = CelTestEnv.Compile("name == \"Alice\" && age > 21");
        var res = Cel2SqlConverter.ConvertParameterized(ast, o => o.WithDialect(Spark));
        res.Sql.Should().Be("name = ? AND age > ?");
        res.Parameters.Should().Equal(new object?[] { "Alice", 21L });
    }

    [Fact]
    public void AnalyzeQueryReturnsEmptyRecommendations()
    {
        // Spark indexing is storage-layer specific; the dialect intentionally
        // returns no recommendations so callers don't get bogus Postgres advice.
        var ast = CelTestEnv.Compile("name == \"Alice\" && age > 21");
        var res = Cel2SqlConverter.AnalyzeQuery(ast, o => o.WithDialect(Spark));
        res.Sql.Should().Be("name = 'Alice' AND age > 21");
        res.Recommendations.Should().BeEmpty();
    }

    [Fact]
    public void ReservedKeywordRejectedAsFieldName()
    {
        // Spark identifier validation rejects reserved keywords (defense in depth).
        var spark = new SparkDialect();
        ((Action)(() => spark.ValidateFieldName("select"))).Should().Throw<ConversionException>();
        ((Action)(() => spark.ValidateFieldName("not"))).Should().Throw<ConversionException>();
    }

    [Fact]
    public void InvalidIdentifierShapeRejected()
    {
        var spark = new SparkDialect();
        ((Action)(() => spark.ValidateFieldName("1bad"))).Should().Throw<ConversionException>();
        ((Action)(() => spark.ValidateFieldName("name space"))).Should().Throw<ConversionException>();
        ((Action)(() => spark.ValidateFieldName(""))).Should().Throw<ConversionException>();
    }

    [Fact]
    public void MultiDimensionalArrayLengthIsRejected()
    {
        // Spark does not support multi-dim ARRAY_LENGTH; the dialect should
        // throw rather than emit invalid SQL.
        var spark = new SparkDialect();
        var sb = new StringBuilder();
        ((Action)(() => spark.WriteArrayLength(sb, 2, () => sb.Append('x'))))
            .Should().Throw<ConversionException>();
    }

    [Fact]
    public void RegexPassesThroughAndRejectsLookahead()
    {
        var spark = new SparkDialect();
        // Pass-through: Java regex == Spark regex, so the pattern is unchanged.
        var ok = spark.ConvertRegex("^[a-z]+$");
        ok.Pattern.Should().Be("^[a-z]+$");

        // Lookahead is rejected (RE2 doesn't support it; defense in depth).
        ((Action)(() => spark.ConvertRegex("(?=foo)bar"))).Should().Throw<ConversionException>();
    }

    [Fact]
    public void RegexCaseInsensitiveInlineFlagIsHonouredByEngine()
    {
        // The (?i) prefix is left in the pattern; Spark's engine honours it natively,
        // so the dialect reports caseInsensitive=false (no separate ~* operator).
        var spark = new SparkDialect();
        var res = spark.ConvertRegex("(?i)Hello");
        res.Pattern.Should().Be("(?i)Hello");
        res.CaseInsensitive.Should().BeFalse();
    }

    [Fact]
    public void JsonArrayMembershipEmitsArrayContains()
    {
        var spark = new SparkDialect();
        var sb = new StringBuilder();
        spark.WriteJsonArrayMembership(sb, "any", () => sb.Append("elem"), () => sb.Append("arr"));
        sb.ToString().Should().Be("array_contains(from_json(arr, 'ARRAY<STRING>'), elem)");

        var sb2 = new StringBuilder();
        spark.WriteNestedJsonArrayMembership(sb2, () => sb2.Append("elem"), () => sb2.Append("arr"));
        sb2.ToString().Should().Be("array_contains(from_json(arr, 'ARRAY<STRING>'), elem)");
    }

    public static IEnumerable<object[]> SparkInListTests()
    {
        yield return new object[]
        {
            "string_in_literal",
            "name in [\"a\", \"b\", \"c\"]",
            "array_contains(array('a', 'b', 'c'), name)"
        };
        yield return new object[]
        {
            "int_in_literal",
            "age in [1, 2, 3]",
            "array_contains(array(1, 2, 3), age)"
        };
    }

    [Theory]
    [MemberData(nameof(SparkInListTests))]
    public void TestSparkInList(string name, string celExpr, string expectedSql)
    {
        var ast = CelTestEnv.Compile(celExpr);
        var sql = Cel2SqlConverter.Convert(ast, o => o.WithDialect(Spark));
        sql.Should().Be(expectedSql, "{0}: CEL '{1}'", name, celExpr);
    }
}
