# Cel2Sql.NET

A .NET library that converts [CEL (Common Expression Language)](https://cel.dev/) expressions into SQL WHERE clauses. Write filter expressions once in CEL and target any supported SQL dialect.

[![NuGet](https://img.shields.io/nuget/v/Cel2Sql.svg)](https://www.nuget.org/packages/Cel2Sql)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Cel2Sql.svg)](https://www.nuget.org/packages/Cel2Sql)
[![CI](https://github.com/SPANDigital/Cel2Sql.NET/actions/workflows/ci.yml/badge.svg)](https://github.com/SPANDigital/Cel2Sql.NET/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)

[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-4169E1?logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![MySQL](https://img.shields.io/badge/MySQL-4479A1?logo=mysql&logoColor=white)](https://www.mysql.com/)
[![SQLite](https://img.shields.io/badge/SQLite-003B57?logo=sqlite&logoColor=white)](https://www.sqlite.org/)
[![DuckDB](https://img.shields.io/badge/DuckDB-FFF000?logo=duckdb&logoColor=black)](https://duckdb.org/)
[![BigQuery](https://img.shields.io/badge/BigQuery-669DF6?logo=googlebigquery&logoColor=white)](https://cloud.google.com/bigquery)
[![Spark](https://img.shields.io/badge/Apache%20Spark-E25A1C?logo=apachespark&logoColor=white)](https://spark.apache.org/)

## Features

- **6 SQL dialects** &mdash; PostgreSQL, MySQL, SQLite, DuckDB, BigQuery, Apache Spark
- **Parameterized queries** &mdash; safe placeholder-based output to prevent SQL injection
- **Index analysis** &mdash; dialect-specific index recommendations for your query patterns
- **JSON/JSONB support** &mdash; field access, existence checks, array operations
- **Array comprehensions** &mdash; `all`, `exists`, `exists_one`, `map`, `filter`
- **Timestamp arithmetic** &mdash; durations, intervals, EXTRACT, timezone handling
- **Regex with ReDoS protection** &mdash; RE2 pattern conversion per dialect
- **Security limits** &mdash; configurable max recursion depth and output length

## Installation

```bash
dotnet add package Cel2Sql
```

Or add a `PackageReference` to your project file:

```xml
<PackageReference Include="Cel2Sql" Version="1.0.0" />
```

**Requirements:** .NET 8.0 or later.

## Quick Start

### Basic Conversion

```csharp
using Cel2Sql;
using Cel2Sql.Cel;

// 1. Build a CEL environment with your variable declarations
var env = CelEnvironment.NewBuilder()
    .AddVariable("name", CelVarType.String)
    .AddVariable("age", CelVarType.Int)
    .Build();

// 2. Compile (parse + type-check) the CEL expression
CelAst ast = env.Compile("name == \"alice\" && age > 21");

// 3. Convert to SQL (defaults to PostgreSQL)
string sql = Cel2SqlConverter.Convert(ast);
// => "name = 'alice' AND age > 21"
```

### Choosing a Dialect

```csharp
using Cel2Sql;
using Cel2Sql.Cel;
using Cel2Sql.Dialects.Postgres;
using Cel2Sql.Dialects.MySql;
using Cel2Sql.Dialects.Sqlite;
using Cel2Sql.Dialects.DuckDb;
using Cel2Sql.Dialects.BigQuery;
using Cel2Sql.Dialects.Spark;

var env = CelEnvironment.NewBuilder()
    .AddVariable("age", CelVarType.Int)
    .Build();
CelAst ast = env.Compile("age > 21");

string pg     = Cel2SqlConverter.Convert(ast);                                       // PostgreSQL (default)
string pgExpl = Cel2SqlConverter.Convert(ast, o => o.WithDialect(new PostgresDialect()));
string mysql  = Cel2SqlConverter.Convert(ast, o => o.WithDialect(new MySqlDialect()));
string sqlite = Cel2SqlConverter.Convert(ast, o => o.WithDialect(new SqliteDialect()));
string duckdb = Cel2SqlConverter.Convert(ast, o => o.WithDialect(new DuckDbDialect()));
string bq     = Cel2SqlConverter.Convert(ast, o => o.WithDialect(new BigQueryDialect()));
string spark  = Cel2SqlConverter.Convert(ast, o => o.WithDialect(new SparkDialect()));
```

### Parameterized Queries

Literal values are replaced with placeholders and returned separately, keeping your queries safe from injection:

```csharp
using Cel2Sql;
using Cel2Sql.Cel;

var env = CelEnvironment.NewBuilder()
    .AddVariable("age", CelVarType.Int)
    .Build();
CelAst ast = env.Compile("age == 18");

ConvertResult result = Cel2SqlConverter.ConvertParameterized(ast);

string sql = result.Sql;
// PostgreSQL: "age = $1"
// MySQL:      "age = ?"

IReadOnlyList<object?> parameters = result.Parameters;
// [18]
```

Booleans and nulls are always inlined (never parameterized) so the database can plan the query optimally.

## Advanced Usage

### Index Analysis

Get dialect-specific index recommendations for your query patterns:

```csharp
using Cel2Sql;
using Cel2Sql.Cel;
using Cel2Sql.Dialects;
using Cel2Sql.Dialects.Postgres;

var env = CelEnvironment.NewBuilder()
    .AddVariable("name", CelVarType.String)
    .AddVariable("age", CelVarType.Int)
    .Build();
CelAst ast = env.Compile("name == \"alice\" && age > 21");

AnalyzeResult result = Cel2SqlConverter.AnalyzeQuery(ast, o => o.WithDialect(new PostgresDialect()));

Console.WriteLine(result.Sql);
// "name = 'alice' AND age > 21"

foreach (IndexRecommendation rec in result.Recommendations)
{
    Console.WriteLine(rec.Column);      // "name", "age"
    Console.WriteLine(rec.IndexType);   // "BTREE"
    Console.WriteLine(rec.Expression);  // "CREATE INDEX idx_name ON table_name (name);"
    Console.WriteLine(rec.Reason);      // why this index helps
}
```

> Index analysis is a no-op on Spark &mdash; `AnalyzeQuery` returns an empty `Recommendations` list because indexing on Spark is storage-layer-specific (Delta Z-order vs Iceberg sort vs plain Parquet) and not portable.

### JSON/JSONB Field Schemas

Define schemas to enable JSON field access and type-aware conversions:

```csharp
using Cel2Sql;
using Cel2Sql.Cel;
using Cel2Sql.Dialects.Postgres;
using Cel2Sql.Schema;

var schema = new Schema(new[]
{
    new FieldSchema("metadata", "jsonb", isJson: true, isJsonb: true),
});

string sql = Cel2SqlConverter.Convert(ast, o => o
    .WithDialect(new PostgresDialect())
    .WithSchemas(new Dictionary<string, Schema> { ["default"] = schema }));
```

### Configuration Options

All options are set via fluent `With...` calls on `ConvertOptions`. Pass them inline as
`params Action<ConvertOptions>[]`, or build a `ConvertOptions` once and reuse it:

```csharp
using Cel2Sql;
using Cel2Sql.Dialects.Postgres;

string sql = Cel2SqlConverter.Convert(ast, o => o
    .WithDialect(new PostgresDialect())                                 // SQL dialect (default: PostgreSQL)
    .WithSchemas(schemas)                                               // Schema map for JSON field detection
    .WithJsonVariables("context", "tags")                              // Flat JSONB columns (use ->> instead of .)
    .WithColumnAliases(new Dictionary<string, string> { ["name"] = "usr_name" }) // CEL identifier -> SQL column rename
    .WithParamStartIndex(5)                                            // Embed in larger query: placeholders start at $5
    .WithMaxDepth(100)                                                 // Max AST recursion depth (default: 100)
    .WithMaxOutputLength(50000)                                        // Max SQL output length (default: 50,000)
    .WithLogger(logger));                                              // ILogger for debugging
```

**Flat JSONB columns** &mdash; mark variables typed as JSONB columns so dot-access uses
JSON operators rather than plain SQL dot notation:

```csharp
// CEL: context.host == "web-1"
// Without:                            context.host = 'web-1'
// With .WithJsonVariables("context"): context->>'host' = 'web-1'
```

**Column aliases** &mdash; map user-facing CEL names to actual database column names
(e.g. when columns are prefixed). Alias values are validated against the dialect's identifier rules:

```csharp
string sql = Cel2SqlConverter.Convert(ast, o => o
    .WithColumnAliases(new Dictionary<string, string>
    {
        ["name"] = "usr_name",
        ["active"] = "usr_active",
    }));
// CEL: name == "Alice"  ->  SQL: usr_name = 'Alice'
```

**Parameter start index** &mdash; useful when embedding the generated SQL into a larger
pre-parameterized query so placeholders don't clash:

```csharp
ConvertResult res = Cel2SqlConverter.ConvertParameterized(ast, o => o.WithParamStartIndex(5));
// PostgreSQL: name = $5 AND age > $6
// Caller appends res.Parameters to their args at positions 5+.
```

### Power users: bring your own Cel.NET AST

If you already hold a type-checked Cel.NET `Ast`, wrap it directly with
`CelAst.FromCelNet(ast)` instead of going through `CelEnvironment`.

## Supported CEL Functions

| Category | Functions |
|---|---|
| String predicates | `contains`, `startsWith`, `endsWith`, `matches`, `size` |
| String manipulation | `lowerAscii`, `upperAscii`, `trim`, `charAt`, `indexOf`, `lastIndexOf`, `substring`, `replace`, `reverse`, `split`, `join`, `format` |
| Type conversion | `bool`, `bytes`, `double`, `int`, `string`, `duration`, `timestamp` |
| Timestamp extraction | `getFullYear`, `getMonth`, `getDate`, `getDayOfMonth`, `getDayOfYear`, `getDayOfWeek`, `getHours`, `getMinutes`, `getSeconds`, `getMilliseconds` |
| Comprehensions | `all`, `exists`, `exists_one`, `map`, `filter` |

> **Note on `format()`** &mdash; supported on PostgreSQL (via `FORMAT()`), BigQuery (via `FORMAT()`),
> SQLite and DuckDB (via `printf()`). MySQL throws because it has no printf-style FORMAT.
> Only `%s`, `%d`, `%f` specifiers are accepted; format strings are bounded to 1000 characters.

## Supported Dialects

| Feature | PostgreSQL | MySQL | SQLite | DuckDB | BigQuery | Spark |
|---|---|---|---|---|---|---|
| Param style | `$N` | `?` | `?` | `$N` | `@pN` | `?` |
| String concat | `\|\|` | `CONCAT()` | `\|\|` | `\|\|` | `\|\|` | `concat()` |
| Array type | native | JSON | JSON | native | native | native (`ARRAY<T>`) |
| Contains | `POSITION()` | `LOCATE()` | `INSTR()` | `CONTAINS()` | `STRPOS()` | `LOCATE()` |
| Regex | `~` / `~*` | `REGEXP` | unsupported | `~` / `~*` | `REGEXP_CONTAINS()` | `RLIKE` (Java regex) |
| JSON access | `->>` | `->>'$.k'` | `json_extract` | `->>` | `JSON_VALUE` | `get_json_object` |
| Index advisor | yes | yes | yes | yes | yes | no (storage-specific) |

## CEL Expression Examples

| CEL Expression | PostgreSQL Output |
|---|---|
| `age > 21` | `age > 21` |
| `name == "alice"` | `name = 'alice'` |
| `active == true` | `active = TRUE` |
| `name == "a" && age > 18` | `name = 'a' AND age > 18` |
| `status == "active" \|\| role == "admin"` | `status = 'active' OR role = 'admin'` |
| `email.startsWith("admin")` | `email LIKE 'admin%' ESCAPE E'\\'` |
| `name.contains("test")` | `POSITION('test' IN name) > 0` |
| `name.matches("^a.*z$")` | `name ~ '^a.*z$'` |
| `"admin" in roles` | `'admin' = ANY(roles)` |
| `scores.all(s, s >= 60)` | `NOT EXISTS (SELECT 1 FROM UNNEST(scores) AS s WHERE NOT (s >= 60))` |
| `age > 18 ? "adult" : "minor"` | `CASE WHEN age > 18 THEN 'adult' ELSE 'minor' END` |

## Resource Limits

Cel2Sql.NET enforces several caps to keep generated SQL safe and bounded:

| Limit | Default | Configurable | Purpose |
|---|---|---|---|
| Recursion depth | 100 | `WithMaxDepth(int)` | Prevent stack overflow on deeply nested CEL |
| SQL output length | 50 000 chars | `WithMaxOutputLength(int)` | Cap total generated SQL size |
| Byte literal length (inline mode) | 10 000 bytes | constant | Prevent CWE-400 hex expansion (parameterized mode bypasses this &mdash; bytes go straight to the parameter list) |
| `format()` string length | 1 000 chars | constant | Bound expansion in formatted SQL |

## Error Handling

Conversion failures throw `ConversionException` (in `Cel2Sql.Errors`), which separates a
`UserMessage` (safe to surface to end users) from `InternalDetails` (for logs only) &mdash;
the CWE-209 pattern.

## Origin

Cel2Sql.NET is a C# port of [cel2sql4j](https://github.com/SPANDigital/cel2sql4j) (the Java port),
which is itself a port of [cel2sql](https://github.com/spandigital/cel2sql), originally written in Go.
It preserves the same API design, dialect coverage, and test cases.

CEL parsing and type-checking are provided by the [Cel.NET](https://github.com/rayokota/cel.net)
library (a .NET port of cel-java).

## License

[MIT](LICENSE) &copy; Span Digital
