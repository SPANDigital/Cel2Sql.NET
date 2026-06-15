using Cel2Sql.Cel;
using Cel2Sql.Dialects.BigQuery;
using Cel2Sql.Dialects.Postgres;
using Cel2Sql.Errors;
using AwesomeAssertions;
using Xunit;

namespace Cel2Sql.Tests;

/// <summary>
/// Tests for the convert options ported from upstream cel2sql v3.7.1:
/// withJsonVariables, withColumnAliases, withParamStartIndex,
/// plus byte-array length cap and the CEL format() string function.
/// Mirrors Cel2SqlOptionsTest.java.
/// </summary>
public class Cel2SqlOptionsTest
{
    private static readonly PostgresDialect PG = new();

    // CEL environment with a flat JSON column (context) plus name.
    private static CelEnvironment JsonVarEnv() =>
        CelEnvironment.NewBuilder()
            .AddVariable("context", CelVarType.Map(CelVarType.String, CelVarType.Dyn))
            .AddVariable("name", CelVarType.String)
            .Build();

    // CEL environment that declares format() as a member function string.format(list) -> string.
    private static CelEnvironment FormatEnv() =>
        CelEnvironment.NewBuilder()
            .AddVariable("name", CelVarType.String)
            .AddVariable("tags", CelVarType.List(CelVarType.String))
            .AddMemberFunction("format", "string_format_list", CelVarType.String,
                CelVarType.String, CelVarType.List(CelVarType.Dyn))
            .Build();

    // ------------------------------------------------------------------
    // WithColumnAliases
    // ------------------------------------------------------------------

    [Fact]
    public void ColumnAliases_RenamesIdentInOutput()
    {
        var ast = CelTestEnv.Compile("name == \"Alice\"");
        var sql = Cel2SqlConverter.Convert(ast, o => o
            .WithDialect(PG)
            .WithColumnAliases(new Dictionary<string, string> { ["name"] = "usr_name" }));
        sql.Should().Be("usr_name = 'Alice'");
    }

    [Fact]
    public void ColumnAliases_AppliesAcrossMultipleIdents()
    {
        var ast = CelTestEnv.Compile("name == \"Alice\" && age > 21");
        var sql = Cel2SqlConverter.Convert(ast, o => o
            .WithDialect(PG)
            .WithColumnAliases(new Dictionary<string, string> { ["name"] = "usr_name", ["age"] = "usr_age" }));
        sql.Should().Be("usr_name = 'Alice' AND usr_age > 21");
    }

    [Fact]
    public void ColumnAliases_ValidatedAgainstDialect()
    {
        var ast = CelTestEnv.Compile("name == \"Alice\"");
        var act = () => Cel2SqlConverter.Convert(ast, o => o
            .WithDialect(PG)
            .WithColumnAliases(new Dictionary<string, string> { ["name"] = "bad name; DROP TABLE users--" }));
        act.Should().Throw<ConversionException>();
    }

    // ------------------------------------------------------------------
    // WithParamStartIndex
    // ------------------------------------------------------------------

    [Fact]
    public void ParamStartIndex_PostgresShiftsPlaceholders()
    {
        var ast = CelTestEnv.Compile("name == \"Alice\" && age > 21");
        var res = Cel2SqlConverter.ConvertParameterized(ast, o => o
            .WithDialect(PG)
            .WithParamStartIndex(5));
        res.Sql.Should().Be("name = $5 AND age > $6");
        res.Parameters.Should().Equal(new object?[] { "Alice", 21L });
    }

    [Fact]
    public void ParamStartIndex_BigqueryShiftsPlaceholders()
    {
        var ast = CelTestEnv.Compile("name == \"Alice\"");
        var res = Cel2SqlConverter.ConvertParameterized(ast, o => o
            .WithDialect(new BigQueryDialect())
            .WithParamStartIndex(7));
        res.Sql.Should().Be("name = @p7");
    }

    [Fact]
    public void ParamStartIndex_ClampedToOne()
    {
        var ast = CelTestEnv.Compile("name == \"Alice\"");
        var res = Cel2SqlConverter.ConvertParameterized(ast, o => o
            .WithDialect(PG)
            .WithParamStartIndex(-3));
        res.Sql.Should().Be("name = $1");
    }

    // ------------------------------------------------------------------
    // WithJsonVariables
    // ------------------------------------------------------------------

    [Fact]
    public void JsonVariables_FlatColumnEmitsArrowOperator()
    {
        var ast = CelTestEnv.Compile(JsonVarEnv(), "context.host == \"web-1\"");
        var sql = Cel2SqlConverter.Convert(ast, o => o
            .WithDialect(PG)
            .WithJsonVariables("context"));
        sql.Should().Be("context->>'host' = 'web-1'");
    }

    [Fact]
    public void JsonVariables_UnmarkedVarUsesPlainDot()
    {
        var ast = CelTestEnv.Compile(JsonVarEnv(), "context.host == \"web-1\"");
        var sql = Cel2SqlConverter.Convert(ast, o => o.WithDialect(PG));
        sql.Should().Be("context.host = 'web-1'");
    }

    // ------------------------------------------------------------------
    // Byte array length cap
    // ------------------------------------------------------------------

    [Fact]
    public void ByteArrayLengthCap_InlineModeRejectsLongLiteral()
    {
        var ast = CelTestEnv.Compile(LongByteLiteral());
        var act = () => Cel2SqlConverter.Convert(ast, o => o.WithDialect(PG));
        var ex = act.Should().Throw<ConversionException>().Which;
        ex.InternalDetails.Should().Contain("byte literal length");
        ex.InternalDetails.Should().Contain("10001");
    }

    [Fact]
    public void ByteArrayLengthCap_ParameterizedModeBypassesCheck()
    {
        var ast = CelTestEnv.Compile(LongByteLiteral());
        var res = Cel2SqlConverter.ConvertParameterized(ast, o => o.WithDialect(PG));
        // Parameterized mode sends bytes directly to the driver — no inlining, no cap.
        res.Sql.Should().Be("$1");
        res.Parameters.Should().HaveCount(1);
        ((byte[])res.Parameters[0]!).Should().HaveCount(10001);
    }

    private static string LongByteLiteral()
    {
        var sb = new System.Text.StringBuilder("b\"");
        for (int i = 0; i < 10001; i++)
        {
            sb.Append("\\x41"); // 'A'
        }
        sb.Append('"');
        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // format()
    // ------------------------------------------------------------------

    public static IEnumerable<object[]> FormatCases()
    {
        // Postgres FORMAT collapses %d/%f to %s for safe coercion.
        yield return new object[] { "format_string_int", "\"%s is %d\".format([\"John\", 30])", TestDialects.PostgreSql, "FORMAT('%s is %s', 'John', 30)" };
        yield return new object[] { "format_string_int", "\"%s is %d\".format([\"John\", 30])", TestDialects.BigQuery, "FORMAT('%s is %d', 'John', 30)" };
        yield return new object[] { "format_string_int", "\"%s is %d\".format([\"John\", 30])", TestDialects.Sqlite, "printf('%s is %d', 'John', 30)" };
        yield return new object[] { "format_string_int", "\"%s is %d\".format([\"John\", 30])", TestDialects.DuckDb, "printf('%s is %d', 'John', 30)" };
        yield return new object[] { "format_string_int", "\"%s is %d\".format([\"John\", 30])", TestDialects.Spark, "format_string('%s is %d', 'John', 30)" };
        yield return new object[] { "format_no_args", "\"hello\".format([])", TestDialects.PostgreSql, "FORMAT('hello')" };
        yield return new object[] { "format_double_percent", "\"100%% sure\".format([])", TestDialects.PostgreSql, "FORMAT('100%% sure')" };
    }

    [Theory]
    [MemberData(nameof(FormatCases))]
    public void TestFormat(string name, string celExpr, string dialectName, string expectedSql)
    {
        var ast = CelTestEnv.Compile(FormatEnv(), celExpr);
        var sql = Cel2SqlConverter.Convert(ast, o => o.WithDialect(TestDialects.Get(dialectName)));
        sql.Should().Be(expectedSql, "{0} [{1}]", name, dialectName);
    }

    [Fact]
    public void Format_MysqlIsExplicitlyUnsupported()
    {
        var ast = CelTestEnv.Compile(FormatEnv(), "\"%s\".format([\"x\"])");
        var act = () => Cel2SqlConverter.Convert(ast, o => o.WithDialect(TestDialects.Get(TestDialects.MySql)));
        act.Should().Throw<ConversionException>();
    }

    [Fact]
    public void Format_UnsupportedSpecifierIsRejected()
    {
        var ast = CelTestEnv.Compile(FormatEnv(), "\"%x\".format([15])");
        var act = () => Cel2SqlConverter.Convert(ast, o => o.WithDialect(PG));
        var ex = act.Should().Throw<ConversionException>().Which;
        ex.InternalDetails.Should().Contain("unsupported specifier");
    }

    [Fact]
    public void Format_ArgCountMismatchIsRejected()
    {
        var ast = CelTestEnv.Compile(FormatEnv(), "\"%s and %s\".format([\"only one\"])");
        var act = () => Cel2SqlConverter.Convert(ast, o => o.WithDialect(PG));
        var ex = act.Should().Throw<ConversionException>().Which;
        ex.InternalDetails.Should().Contain("argument count mismatch");
    }

    [Fact]
    public void Format_DynamicFormatStringIsRejected()
    {
        var ast = CelTestEnv.Compile(FormatEnv(), "name.format([])");
        var act = () => Cel2SqlConverter.Convert(ast, o => o.WithDialect(PG));
        act.Should().Throw<ConversionException>();
    }

    // ------------------------------------------------------------------
    // Composing options
    // ------------------------------------------------------------------

    [Fact]
    public void Aliases_AndParamStartIndex_ComposeCleanly()
    {
        var ast = CelTestEnv.Compile("name == \"Alice\" && age > 21");
        var res = Cel2SqlConverter.ConvertParameterized(ast, o => o
            .WithDialect(PG)
            .WithColumnAliases(new Dictionary<string, string> { ["name"] = "usr_name" })
            .WithParamStartIndex(10));
        res.Sql.Should().Be("usr_name = $10 AND age > $11");
        res.Parameters.Should().Equal(new object?[] { "Alice", 21L });
    }
}
