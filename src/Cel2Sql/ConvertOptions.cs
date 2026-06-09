using Cel2Sql.Dialects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SchemaType = Cel2Sql.Schema.Schema;

namespace Cel2Sql;

/// <summary>
/// Configuration options for CEL to SQL conversion.
/// Uses a fluent builder pattern; pass configurators as <c>Action&lt;ConvertOptions&gt;</c>.
/// </summary>
public sealed class ConvertOptions
{
    private const int DefaultMaxDepth = 100;
    private const int DefaultMaxOutputLength = 50000;

    public IReadOnlyDictionary<string, SchemaType>? Schemas { get; private set; }
    public ILogger Logger { get; private set; } = NullLogger.Instance;
    public int MaxDepth { get; private set; } = DefaultMaxDepth;
    public int MaxOutputLength { get; private set; } = DefaultMaxOutputLength;
    public IDialect? Dialect { get; private set; }
    public IReadOnlySet<string> JsonVariables { get; private set; } = new HashSet<string>();
    public IReadOnlyDictionary<string, string> ColumnAliases { get; private set; } = new Dictionary<string, string>();
    public int ParamStartIndex { get; private set; } = 1;

    private ConvertOptions() { }

    /// <summary>Creates a new ConvertOptions with default settings, then applies the configurator.</summary>
    public static ConvertOptions Configure(Action<ConvertOptions> configurator)
    {
        var options = new ConvertOptions();
        configurator(options);
        return options;
    }

    /// <summary>Creates a new ConvertOptions with default settings.</summary>
    public static ConvertOptions Defaults() => new();

    /// <summary>Sets the schema map for JSON/JSONB field detection.</summary>
    public ConvertOptions WithSchemas(IReadOnlyDictionary<string, SchemaType> schemas)
    {
        Schemas = schemas;
        return this;
    }

    /// <summary>Sets the logger for observability and debugging.</summary>
    public ConvertOptions WithLogger(ILogger logger)
    {
        Logger = logger;
        return this;
    }

    /// <summary>Sets the maximum recursion depth.</summary>
    public ConvertOptions WithMaxDepth(int maxDepth)
    {
        MaxDepth = maxDepth;
        return this;
    }

    /// <summary>Sets the maximum SQL output length.</summary>
    public ConvertOptions WithMaxOutputLength(int maxOutputLength)
    {
        MaxOutputLength = maxOutputLength;
        return this;
    }

    /// <summary>Sets the SQL dialect.</summary>
    public ConvertOptions WithDialect(IDialect dialect)
    {
        Dialect = dialect;
        return this;
    }

    /// <summary>
    /// Declares CEL variable names that correspond to flat JSONB columns.
    /// When marked, dot/bracket access emits JSONB text-extraction operators
    /// (e.g. <c>context-&gt;&gt;'host'</c>) instead of plain dot notation.
    /// </summary>
    public ConvertOptions WithJsonVariables(params string[] vars)
    {
        if (vars == null || vars.Length == 0)
        {
            JsonVariables = new HashSet<string>();
            return this;
        }
        JsonVariables = new HashSet<string>(vars);
        return this;
    }

    /// <summary>
    /// Maps CEL identifier names to SQL column names. When a CEL identifier matches a key,
    /// the SQL output uses the mapped column name.
    /// </summary>
    public ConvertOptions WithColumnAliases(IReadOnlyDictionary<string, string> aliases)
    {
        if (aliases == null || aliases.Count == 0)
        {
            ColumnAliases = new Dictionary<string, string>();
            return this;
        }
        ColumnAliases = new Dictionary<string, string>(aliases);
        return this;
    }

    /// <summary>
    /// Sets the first placeholder index for parameterized conversion. Use when embedding
    /// a CEL fragment into a larger parameterized query. Default is 1; values &lt; 1 clamp to 1.
    /// </summary>
    public ConvertOptions WithParamStartIndex(int index)
    {
        ParamStartIndex = Math.Max(1, index);
        return this;
    }
}
