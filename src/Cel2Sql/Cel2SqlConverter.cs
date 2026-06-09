using Cel2Sql.Cel;
using Cel2Sql.Dialects;
using Cel2Sql.Dialects.Postgres;
using Cel2Sql.Errors;

namespace Cel2Sql;

/// <summary>
/// Public API for converting CEL (Common Expression Language) expressions to SQL WHERE clauses.
/// Provides three conversion modes: inline literals, parameterized queries, and analysis with
/// index recommendations. Defaults to PostgreSQL when no dialect is specified.
/// </summary>
/// <example>
/// <code>
/// var env = CelEnvironment.NewBuilder().AddVariable("age", CelVarType.Int).Build();
/// var ast = env.Compile("age > 18");
/// string sql = Cel2SqlConverter.Convert(ast);                       // "age > 18"
/// ConvertResult r = Cel2SqlConverter.ConvertParameterized(ast);     // "age > $1", [18]
/// string pg = Cel2SqlConverter.Convert(ast, o => o.WithDialect(new PostgresDialect()));
/// </code>
/// </example>
public static class Cel2SqlConverter
{
    /// <summary>Converts a CEL AST to a SQL WHERE clause with inline literal values.</summary>
    public static string Convert(CelAst ast, params Action<ConvertOptions>[] options)
    {
        var opts = BuildOptions(options);
        var converter = new Converter(ast, opts, parameterize: false);
        return converter.Convert();
    }

    /// <summary>Converts a CEL AST to a SQL WHERE clause with a pre-built <see cref="ConvertOptions"/>.</summary>
    public static string Convert(CelAst ast, ConvertOptions opts)
    {
        if (opts.Dialect == null) opts.WithDialect(new PostgresDialect());
        var converter = new Converter(ast, opts, parameterize: false);
        return converter.Convert();
    }

    /// <summary>
    /// Converts a CEL AST to a parameterized SQL WHERE clause. Literal values become
    /// placeholders ($1, $2, …) returned separately. Booleans and nulls are always inlined.
    /// </summary>
    public static ConvertResult ConvertParameterized(CelAst ast, params Action<ConvertOptions>[] options)
    {
        var opts = BuildOptions(options);
        var converter = new Converter(ast, opts, parameterize: true);
        string sql = converter.Convert();
        return new ConvertResult(sql, converter.GetParameters());
    }

    /// <summary>Parameterized conversion with a pre-built <see cref="ConvertOptions"/>.</summary>
    public static ConvertResult ConvertParameterized(CelAst ast, ConvertOptions opts)
    {
        if (opts.Dialect == null) opts.WithDialect(new PostgresDialect());
        var converter = new Converter(ast, opts, parameterize: true);
        string sql = converter.Convert();
        return new ConvertResult(sql, converter.GetParameters());
    }

    /// <summary>Converts a CEL AST to SQL and provides dialect-specific index recommendations.</summary>
    public static AnalyzeResult AnalyzeQuery(CelAst ast, params Action<ConvertOptions>[] options)
    {
        var opts = BuildOptions(options);
        return AnalyzeInternal(ast, opts);
    }

    /// <summary>Analysis with a pre-built <see cref="ConvertOptions"/>.</summary>
    public static AnalyzeResult AnalyzeQuery(CelAst ast, ConvertOptions opts)
    {
        if (opts.Dialect == null) opts.WithDialect(new PostgresDialect());
        return AnalyzeInternal(ast, opts);
    }

    private static AnalyzeResult AnalyzeInternal(CelAst ast, ConvertOptions opts)
    {
        string sql = Convert(ast, opts);
        IIndexAdvisor advisor = opts.Dialect is IIndexAdvisor a ? a : new PostgresDialect();
        var converter = new Converter(ast, opts, parameterize: false);
        var recommendations = converter.CollectIndexRecommendations(advisor);
        return new AnalyzeResult(sql, recommendations);
    }

    private static ConvertOptions BuildOptions(Action<ConvertOptions>[] options)
    {
        var opts = ConvertOptions.Defaults();
        foreach (var opt in options) opt(opts);
        if (opts.Dialect == null) opts.WithDialect(new PostgresDialect());
        return opts;
    }
}
