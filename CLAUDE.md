# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

Cel2Sql.NET is a C# library that converts CEL (Common Expression Language) expressions into SQL WHERE clauses across six SQL dialects. It is a faithful port of the Java library `cel2sql4j`, which is itself a port of the Go library `github.com/spandigital/cel2sql`.

Reference sources:
- Java port (the direct upstream for this port): `/Users/richardwooding/Code/SPAN/cel2sql4j`
- Go upstream (original): `/Users/richardwooding/Code/SPAN/cel2sql`

When porting a feature or fix, prefer mirroring the Java implementation (`Converter.java`, the dialect classes) because the C# converter is a near-mechanical translation of it.

## Build & Test Commands

```bash
dotnet build                                              # Build the solution
dotnet test test/Cel2Sql.Tests/Cel2Sql.Tests.csproj      # Unit tests
dotnet test test/Cel2Sql.IntegrationTests/Cel2Sql.IntegrationTests.csproj  # Integration tests (Docker required for PG/MySQL)

# Run a single test (xUnit filter)
dotnet test test/Cel2Sql.Tests/Cel2Sql.Tests.csproj --filter "FullyQualifiedName~Cel2SqlBasicTest"
dotnet test test/Cel2Sql.Tests/Cel2Sql.Tests.csproj --filter "DisplayName~startsWith"
```

### SDK / framework notes

- The library targets **net8.0** (`Directory.Build.props`). Test projects also target net8.0.
- The solution file is **`Cel2Sql.slnx`** (the XML solution format), which requires a **.NET 9+ SDK** to load.
- Locally, only the **.NET 10 SDK** may be installed. The test projects set `RollForward=Major` so the net8.0 test host runs on the .NET 10 runtime. CI installs both 8.0.x and 10.0.x.
- Central package management is on (`Directory.Packages.props`); add/bump versions there, not in individual csproj files.

## Architecture

### Public API (`src/Cel2Sql/Cel2SqlConverter.cs`)

Static class with three entry points, each with a `params Action<ConvertOptions>[]` overload and a pre-built `ConvertOptions` overload:
- `Convert(ast, ...)` &mdash; inline literals in the SQL string
- `ConvertParameterized(ast, ...)` &mdash; placeholders + extracted parameter list (`ConvertResult`)
- `AnalyzeQuery(ast, ...)` &mdash; SQL + dialect-specific index recommendations (`AnalyzeResult`)

All default to PostgreSQL when no dialect is specified. Options use the fluent builder in `ConvertOptions.cs`: `WithDialect / WithSchemas / WithJsonVariables / WithColumnAliases / WithParamStartIndex / WithMaxDepth / WithMaxOutputLength / WithLogger`.

Result types are records: `ConvertResult(Sql, Parameters)`, `AnalyzeResult(Sql, Recommendations)`, `IndexRecommendation(Column, IndexType, Expression, Reason)`.

### Core Conversion (`src/Cel2Sql/Converter.cs`)

Single-pass AST visitor. Walks `CelExprNode` nodes via `Visit()` dispatch on `node.Kind` (Constant, Ident, Select, Call, List, Comprehension, Struct, Map). Key internal concerns:

- **Operator precedence** drives parenthesization decisions.
- **Parameterization** &mdash; booleans and nulls are always inlined (never parameterized) for query-plan optimization.
- **Depth/length guards** &mdash; `MaxDepth` (default 100) and `MaxOutputLength` (default 50000) prevent runaway queries.
- **`SqlWriter` callback pattern** &mdash; dialect methods receive `SqlWriter` delegates (closures that append to the shared `StringBuilder`) instead of pre-rendered strings, so dialect code controls output ordering.

### CEL Adapter (`src/Cel2Sql/Cel/`)

This is the layer that isolates the converter from the underlying CEL implementation (Cel.NET) so the converter can mirror the Java `dev.cel` accessor surface:

- `CelEnvironment` / `CelEnvironmentBuilder` &mdash; wraps Cel.NET's `Env`. `NewBuilder().AddVariable(name, CelVarType).Build().Compile(src)` parses + type-checks and returns a `CelAst`. `AddMemberFunction` declares receiver-style functions.
- `CelVarType` &mdash; factory methods (`String/Int/Uint/Bool/Double/Bytes/Timestamp/Duration/Dyn/List/Map`) over the Cel.NET proto `Type`; insulates callers from proto types.
- `CelAst` &mdash; equivalent to dev.cel's `CelAbstractSyntaxTree`. Built from a Cel.NET `Ast` via `CelAst.FromCelNet(ast)`, which reads the per-node type map from the proto `CheckedExpr` (`AstToCheckedExpr().TypeMap`). `GetType(exprId)` mirrors `ast.getType(id).orElse(null)`.
- `CelExprNode` &mdash; wraps the proto `Expr` and presents the same accessor surface as dev.cel's `CelExpr` (`Id`, `Kind`, call/select/list/comprehension/struct accessors).
- `CelTypeRef` / `CelEnums` &mdash; collapse the proto type representation to the small surface the converter needs (kind + list element type), mirroring dev.cel `CelType`/`ListType`/`MapType`.

### Dialect System (`src/Cel2Sql/Dialects/`)

`IDialect` interface (~35 methods) organized by: literals, operators, type casting, arrays, JSON, timestamps, string functions, comprehensions, structs, validation, regex, capabilities. `DialectBase` is an abstract base providing the one default behaviour (`WriteComprehensionSource`) that the Java port expressed as an interface default method; dialects override it as needed.

The dialect name lives in the `DialectName` enum. Each dialect lives in `Cel2Sql.Dialects.<X>` with:
- `XxxDialect.cs` &mdash; implements `IDialect` (via `DialectBase`) and, where applicable, `IIndexAdvisor`.
- `XxxValidation.cs` &mdash; field name validation, reserved keyword set.
- `XxxRegex.cs` &mdash; RE2 -> dialect-native regex conversion with ReDoS protection (except SQLite, which doesn't support regex).

Six dialects: `Postgres`, `MySql`, `Sqlite`, `DuckDb`, `BigQuery`, `Spark`. Default is PostgreSQL.

Key dialect differences to be aware of:

| Feature | PostgreSQL | MySQL | SQLite | DuckDB | BigQuery | Spark |
|---|---|---|---|---|---|---|
| Param style | `$N` | `?` | `?` | `$N` | `@pN` | `?` |
| String concat | `\|\|` | `CONCAT()` | `\|\|` | `\|\|` | `\|\|` | `concat()` |
| Array type | native | JSON | JSON | native | native | native (`ARRAY<T>`) |
| Contains | `POSITION()` | `LOCATE()` | `INSTR()` | `CONTAINS()` | `STRPOS()` | `LOCATE()` |
| Regex | `~` / `~*` | `REGEXP` | unsupported | `~` / `~*` | `REGEXP_CONTAINS()` | `RLIKE` (Java regex) |
| JSON access | `->>` | `->>'$.k'` | `json_extract` | `->>` | `JSON_VALUE` | `get_json_object` |
| Index advisor | yes | yes | yes | yes | yes | n/a (storage-specific) |

Spark notes: array length uses `COALESCE(size(arr), 0)` so `size(null)` is 0; `getDayOfWeek` emits `(dayofweek(...) - 1)` to convert Spark's 1=Sunday convention to CEL's 0=Sunday convention; string concat uses `concat()`. Index analysis is intentionally a no-op on Spark &mdash; `AnalyzeQuery` returns an empty recommendations list because indexing on Spark is storage-layer-specific (Delta Z-order vs Iceberg sort vs plain Parquet) and not portable.

### Error Handling

`ConversionException` (`src/Cel2Sql/Errors/`) separates `UserMessage` (safe for end users) from `InternalDetails` (for logs only) &mdash; the CWE-209 pattern. Factory methods: `ConversionException.Of(userMsg, details)` and `ConversionException.Wrap(userMsg, cause)`.

### Schema System (`src/Cel2Sql/Schema/`)

`FieldSchema` (record) and `Schema` describe table columns for JSON/JSONB field detection. `Schema` keeps a list for ordered iteration plus a dictionary index for O(1) `FindField`. Passed via `ConvertOptions.WithSchemas()`. When schemas are absent, the converter treats all fields as plain columns.

`ConvertOptions.WithJsonVariables(params string[])` is the alternative for **flat JSONB columns** &mdash; mark a variable name as a JSONB column and any dot/bracket access on it emits `->>` text-extraction operators (e.g. `context.host` -> `context->>'host'`). `WithColumnAliases(IReadOnlyDictionary<string,string>)` rewrites CEL identifier names to differently-named DB columns at emit time (alias values are validated against the dialect's identifier rules). `WithParamStartIndex(int)` shifts the placeholder counter so a generated CEL fragment can be embedded in a larger pre-parameterized query.

## Differences from the Java cel2sql4j / Go upstream

- **Built on Cel.NET (rayokota/cel.net), not Google `dev.cel`.** Cel.NET is a .NET port of cel-java. The CEL adapter in `src/Cel2Sql/Cel/` isolates this dependency so `Converter.cs` reads like the Java converter (same accessor names, same `GetType(id)` shape).
- **Cel.NET pulls heavy transitive dependencies** &mdash; Grpc, Avro, NodaTime, Newtonsoft.Json, and System.Drawing.Common. The last is **pinned to a patched 8.0.x in `Directory.Packages.props`** because Cel.NET would otherwise pull 4.7.0, which carries advisory GHSA-rxg9-xrhp-64gj.
- **Public API takes a `CelAst` wrapper**, not a Cel.NET proto type, insulating consumers from the underlying proto/AST types. Power users with their own Cel.NET `Ast` use `CelAst.FromCelNet(ast)`.
- **C# idioms.** Records (`ConvertResult`, `AnalyzeResult`, `IndexRecommendation`, `FieldSchema`), nullable reference types, `params Action<ConvertOptions>[]` instead of Java varargs `Consumer`, and `IDialect` + abstract `DialectBase` instead of an interface-with-default-methods.
- **Targets net8.0.**
- Like cel2sql4j: **no name-based numeric cast heuristic** (explicit `int(x)`/`double(x)` required); a **single `ConversionException`** instead of upstream Go's 16 sentinel errors; **no JDBC/ADO schema loaders** (users construct `Schema` directly); **`format()` is dialect-restricted** (PostgreSQL/BigQuery `FORMAT()`, SQLite/DuckDB `printf()`, MySQL throws; only `%s`/`%d`/`%f`).

## Test Patterns

Unit tests (`test/Cel2Sql.Tests/`) use xUnit `[Theory]` + `[MemberData]` providing `IEnumerable<object[]>`.

- `CelTestEnv` provides a `Standard` environment pre-loaded with common variable declarations (name:string, age:int, adult:bool, height:double, tags:list<string>, scores:list<int>, created_at:timestamp, etc.) and `Compile(celExpr)` to get a checked `CelAst`. Mirrors the Java `CelHelper`.
- `TestDialects` is a registry mapping a serializable dialect **name** string (e.g. `TestDialects.PostgreSql`) to an `IDialect` instance via `ByName`. Theory data carries the name (so it shows up in test output); the test resolves the instance.
- To add a test case, add an entry (test name, CEL expression, expected SQL) to the relevant `*Test` member-data source.

Integration tests (`test/Cel2Sql.IntegrationTests/`) execute generated SQL against real engines: PostgreSQL and MySQL via Testcontainers (Docker required), SQLite and DuckDB embedded (in-process). `IntegrationCelEnv`, `IntegrationCatalog`, and `IntegrationTestCase` set up the shared fixtures.

## CI/CD

### CI (`.github/workflows/ci.yml`)

Runs on push to `main` and PRs targeting `main`:
- **build** &mdash; sets up .NET 8.0.x + 10.0.x, `dotnet restore` / `build -c Release` / unit tests; uploads `*.trx` results (14-day retention).
- **integration-test** &mdash; runs after build, executes the integration test project (Testcontainers PG/MySQL + embedded SQLite/DuckDB); uploads results.

### Release (`.github/workflows/release.yml`)

Triggered by pushing a tag matching `v*`:
1. Validates tag format (`vX.Y.Z` or `vX.Y.Z-qualifier`).
2. Runs unit tests.
3. `dotnet pack` with `-p:Version=<tag-without-v>`.
4. `dotnet nuget push` to NuGet.org (requires the `NUGET_API_KEY` secret).
5. Creates a GitHub Release with auto-generated notes (qualifier tags marked prerelease).

### Cutting a Release

```bash
git tag -a v0.2.0 -m "Release v0.2.0 - description"
git push origin v0.2.0
gh run watch
```

### Required Secrets

| Secret | Purpose |
|---|---|
| `NUGET_API_KEY` | NuGet.org API key used by `dotnet nuget push` |

### Dependabot (`.github/dependabot.yml`)

Weekly updates for the `nuget` and `github-actions` ecosystems.
