using System.Text;
using Cel2Sql.Errors;

namespace Cel2Sql.Dialects.Spark;

/// <summary>
/// Apache Spark SQL dialect implementation.
///
/// <para>Ported from the Go <c>dialect/spark/dialect.go</c> implementation. Spark
/// runs on the JVM and uses <c>java.util.regex.Pattern</c>, so the regex
/// translator is mostly a passthrough. Spark has no separate JSONB type — JSON
/// fields are accessed via <c>get_json_object</c>; arrays use the native
/// <c>ARRAY&lt;T&gt;</c> type with <c>array_contains</c> / <c>size</c> / <c>EXPLODE</c>.</para>
///
/// <para>Spark does not implement <see cref="IIndexAdvisor"/>: indexing on Spark is
/// storage-layer-specific (Delta Z-order vs Iceberg sort vs plain Parquet) and not
/// portable as a single set of SQL recommendations. <see cref="SupportsIndexAnalysis"/>
/// is <c>false</c>, so <c>analyzeQuery</c> returns an empty recommendation list.</para>
/// </summary>
public sealed class SparkDialect : DialectBase, IIndexAdvisor
{
    public SparkDialect()
    {
    }

    public override DialectName Name => DialectName.Spark;

    // --- Literals ---

    public override void WriteStringLiteral(StringBuilder w, string value)
    {
        string escaped = value.Replace("'", "''");
        w.Append('\'').Append(escaped).Append('\'');
    }

    public override void WriteBytesLiteral(StringBuilder w, byte[] value)
    {
        w.Append("X'");
        w.Append(Convert.ToHexString(value));
        w.Append('\'');
    }

    /// <summary>
    /// Writes a positional placeholder (<c>?</c>). Spark JDBC uses positional
    /// parameters, so the index argument is unused (the converter relies on
    /// parameter list order to correlate values).
    /// </summary>
    public override void WriteParamPlaceholder(StringBuilder w, int paramIndex)
    {
        w.Append('?');
    }

    // --- Operators ---

    public override void WriteStringConcat(StringBuilder w, SqlWriter writeLhs, SqlWriter writeRhs)
    {
        // concat() works in all Spark versions; the || operator was added in 3.0+.
        w.Append("concat(");
        writeLhs();
        w.Append(", ");
        writeRhs();
        w.Append(')');
    }

    public override void WriteRegexMatch(StringBuilder w, SqlWriter writeTarget, string pattern, bool caseInsensitive)
    {
        // Spark regex uses Java pattern syntax; (?i) inline flag is honoured by the
        // engine, so caseInsensitive is folded into the pattern by SparkRegex.
        writeTarget();
        w.Append(" RLIKE '");
        string escaped = pattern.Replace("'", "''");
        w.Append(escaped);
        w.Append('\'');
    }

    public override void WriteLikeEscape(StringBuilder w)
    {
        w.Append(" ESCAPE '\\\\'");
    }

    public override void WriteArrayMembership(StringBuilder w, SqlWriter writeElem, SqlWriter writeArray)
    {
        w.Append("array_contains(");
        writeArray();
        w.Append(", ");
        writeElem();
        w.Append(')');
    }

    // --- Type Casting ---

    public override void WriteCastToNumeric(StringBuilder w)
    {
        // Spark has no postfix `::TYPE` cast; arithmetic coercion `+ 0` works
        // (same trick MySQL/SQLite use), forcing string→number coercion.
        w.Append(" + 0");
    }

    public override void WriteTypeName(StringBuilder w, string celTypeName)
    {
        switch (celTypeName)
        {
            case "bool":
                w.Append("BOOLEAN");
                break;
            case "bytes":
                w.Append("BINARY");
                break;
            case "double":
                w.Append("DOUBLE");
                break;
            case "int":
                w.Append("BIGINT");
                break;
            case "string":
                w.Append("STRING");
                break;
            case "uint":
                w.Append("BIGINT");
                break;
            default:
                w.Append(celTypeName.ToUpperInvariant());
                break;
        }
    }

    public override void WriteEpochExtract(StringBuilder w, SqlWriter writeExpr)
    {
        w.Append("UNIX_TIMESTAMP(");
        writeExpr();
        w.Append(')');
    }

    public override void WriteTimestampCast(StringBuilder w, SqlWriter writeExpr)
    {
        w.Append("CAST(");
        writeExpr();
        w.Append(" AS TIMESTAMP)");
    }

    // --- Arrays ---

    public override void WriteArrayLiteralOpen(StringBuilder w)
    {
        w.Append("array(");
    }

    public override void WriteArrayLiteralClose(StringBuilder w)
    {
        w.Append(')');
    }

    public override void WriteArrayLength(StringBuilder w, int dimension, SqlWriter writeExpr)
    {
        if (dimension > 1)
        {
            throw ConversionException.Of("Unsupported feature",
                "Spark dialect does not support multi-dimensional array length (dimension=" + dimension + ")");
        }
        // Spark size() returns -1 for null; COALESCE collapses to 0 to match cel2sql semantics.
        w.Append("COALESCE(size(");
        writeExpr();
        w.Append("), 0)");
    }

    public override void WriteListIndex(StringBuilder w, SqlWriter writeArray, SqlWriter writeIndex)
    {
        writeArray();
        w.Append('[');
        writeIndex();
        w.Append(']');
    }

    public override void WriteListIndexConst(StringBuilder w, SqlWriter writeArray, long index)
    {
        writeArray();
        w.Append('[').Append(index).Append(']');
    }

    public override void WriteEmptyTypedArray(StringBuilder w, string typeName)
    {
        w.Append("CAST(array() AS ARRAY<").Append(SparkTypeName(typeName)).Append(">)");
    }

    // --- JSON ---

    public override void WriteJsonFieldAccess(StringBuilder w, SqlWriter writeBase, string fieldName, bool isFinal)
    {
        // Spark's get_json_object always returns a string; the same function is used
        // for both intermediate and final access (Spark has no JSON_QUERY equivalent).
        w.Append("get_json_object(");
        writeBase();
        w.Append(", '$.").Append(EscapeJsonFieldName(fieldName)).Append("')");
    }

    public override void WriteJsonExistence(StringBuilder w, bool isJsonb, string fieldName, SqlWriter writeBase)
    {
        w.Append("get_json_object(");
        writeBase();
        w.Append(", '$.").Append(EscapeJsonFieldName(fieldName)).Append("') IS NOT NULL");
    }

    public override void WriteJsonArrayElements(StringBuilder w, bool isJsonb, bool asText, SqlWriter writeExpr)
    {
        // Element type is fixed to STRING; numeric comparisons coerce via WriteCastToNumeric.
        w.Append("EXPLODE(from_json(");
        writeExpr();
        w.Append(", 'ARRAY<STRING>'))");
    }

    public override void WriteJsonArrayLength(StringBuilder w, SqlWriter writeExpr)
    {
        w.Append("COALESCE(size(from_json(");
        writeExpr();
        w.Append(", 'ARRAY<STRING>')), 0)");
    }

    public override void WriteJsonExtractPath(StringBuilder w, IReadOnlyList<string> pathSegments, SqlWriter writeRoot)
    {
        w.Append("get_json_object(");
        writeRoot();
        w.Append(", '$");
        foreach (string segment in pathSegments)
        {
            w.Append('.').Append(EscapeJsonFieldName(segment));
        }
        w.Append("') IS NOT NULL");
    }

    /// <summary>
    /// JSON array membership (<c>elem in jsonArrayField</c>) on Spark.
    ///
    /// <para>When the column is typed as a native <c>ARRAY&lt;T&gt;</c> (rather than a JSON
    /// string parsed via <c>from_json</c>), the standard array-membership path
    /// (<see cref="WriteArrayMembership"/>) is used and emits <c>array_contains(arr, elem)</c>,
    /// which works correctly.</para>
    /// </summary>
    public override void WriteJsonArrayMembership(StringBuilder w, string jsonFunc, SqlWriter writeElem, SqlWriter writeArray)
    {
        w.Append("array_contains(from_json(");
        writeArray();
        w.Append(", 'ARRAY<STRING>'), ");
        writeElem();
        w.Append(')');
    }

    public override void WriteNestedJsonArrayMembership(StringBuilder w, SqlWriter writeElem, SqlWriter writeArray)
    {
        w.Append("array_contains(from_json(");
        writeArray();
        w.Append(", 'ARRAY<STRING>'), ");
        writeElem();
        w.Append(')');
    }

    // --- Timestamps ---

    public override void WriteDuration(StringBuilder w, long value, string unit)
    {
        w.Append("INTERVAL ").Append(value).Append(' ').Append(unit);
    }

    public override void WriteInterval(StringBuilder w, SqlWriter writeValue, string unit)
    {
        w.Append("INTERVAL ");
        writeValue();
        w.Append(' ').Append(unit);
    }

    public override void WriteExtract(StringBuilder w, string part, SqlWriter writeExpr, SqlWriter writeTz)
    {
        // Spark dayofweek() returns 1=Sunday..7=Saturday; CEL convention is 0=Sunday..6=Saturday.
        bool isDow = "DOW".Equals(part, StringComparison.Ordinal);
        if (isDow)
        {
            w.Append("(dayofweek(");
            writeExpr();
            if (writeTz != null)
            {
                w.Append(" AT TIME ZONE ");
                writeTz();
            }
            w.Append(") - 1)");
            return;
        }
        w.Append("EXTRACT(").Append(part).Append(" FROM ");
        writeExpr();
        if (writeTz != null)
        {
            w.Append(" AT TIME ZONE ");
            writeTz();
        }
        w.Append(')');
    }

    public override void WriteTimestampArithmetic(StringBuilder w, string op, SqlWriter writeTs, SqlWriter writeDur)
    {
        writeTs();
        w.Append(' ').Append(op).Append(' ');
        writeDur();
    }

    // --- String Functions ---

    public override void WriteContains(StringBuilder w, SqlWriter writeHaystack, SqlWriter writeNeedle)
    {
        // LOCATE(substr, str) returns 1-based position or 0 when not found.
        w.Append("LOCATE(");
        writeNeedle();
        w.Append(", ");
        writeHaystack();
        w.Append(") > 0");
    }

    public override void WriteSplit(StringBuilder w, SqlWriter writeStr, SqlWriter writeDelim)
    {
        w.Append("split(");
        writeStr();
        w.Append(", ");
        writeDelim();
        w.Append(')');
    }

    public override void WriteSplitWithLimit(StringBuilder w, SqlWriter writeStr, SqlWriter writeDelim, long limit)
    {
        // Spark 3.x+ supports the 3-arg split.
        w.Append("split(");
        writeStr();
        w.Append(", ");
        writeDelim();
        w.Append(", ").Append(limit).Append(')');
    }

    public override void WriteJoin(StringBuilder w, SqlWriter writeArray, SqlWriter writeDelim)
    {
        w.Append("array_join(");
        writeArray();
        w.Append(", ");
        if (writeDelim != null)
        {
            writeDelim();
        }
        else
        {
            w.Append("''");
        }
        w.Append(')');
    }

    public override void WriteFormat(StringBuilder w, string formatSpec, IReadOnlyList<SqlWriter> writeArgs)
    {
        // Spark's format_string() is its printf-equivalent (supports %s/%d/%f directly).
        w.Append("format_string(");
        WriteStringLiteral(w, formatSpec);
        foreach (SqlWriter arg in writeArgs)
        {
            w.Append(", ");
            arg();
        }
        w.Append(')');
    }

    // --- Comprehensions ---

    public override void WriteUnnest(StringBuilder w, SqlWriter writeSource)
    {
        // The converter wraps this in subquery scaffolding. Spark uses EXPLODE for the
        // SELECT FROM UNNEST() pattern; the surrounding (SELECT collect_list(...)) wrapper
        // (see WriteArraySubqueryOpen) re-collects the rows into an array.
        w.Append("EXPLODE(");
        writeSource();
        w.Append(')');
    }

    public override void WriteArraySubqueryOpen(StringBuilder w)
    {
        // Spark has no ARRAY(SELECT ...) constructor; collect_list() is the closest equivalent.
        w.Append("(SELECT collect_list(");
    }

    public override void WriteArraySubqueryExprClose(StringBuilder w)
    {
        w.Append(')');
    }

    // --- Struct ---

    public override void WriteStructOpen(StringBuilder w)
    {
        w.Append("struct(");
    }

    public override void WriteStructClose(StringBuilder w)
    {
        w.Append(')');
    }

    // --- Validation ---

    public override int MaxIdentifierLength => SparkValidation.MaxIdentifierLength;

    public override void ValidateFieldName(string name)
    {
        SparkValidation.ValidateFieldName(name);
    }

    public override IReadOnlySet<string> ReservedKeywords => SparkValidation.GetReservedKeywords();

    // --- Regex ---

    public override RegexResult ConvertRegex(string re2Pattern)
    {
        return SparkRegex.ConvertRe2ToSpark(re2Pattern);
    }

    public override bool SupportsRegex => true;

    // --- Capabilities ---

    public override bool SupportsNativeArrays => true;

    public override bool SupportsJsonb => false;

    public override bool SupportsIndexAnalysis =>
        // Spark indexing is storage-layer-specific (Delta Z-order vs Iceberg sort vs
        // plain Parquet) and not portable as a single set of SQL recommendations.
        false;

    // --- Index advisor ---
    // Spark implements IIndexAdvisor but always returns null / no patterns: indexing on
    // Spark is storage-layer-specific and not portable. Implementing the interface (rather
    // than omitting it) matches the Java behavior so AnalyzeQuery uses Spark itself as the
    // advisor and returns an empty recommendation list, instead of falling back to Postgres.

    public IndexRecommendation? RecommendIndex(IndexPattern pattern) => null;

    public IReadOnlyList<PatternType> SupportedPatterns() => Array.Empty<PatternType>();

    // --- Internal helpers ---

    private static string EscapeJsonFieldName(string fieldName)
    {
        return fieldName.Replace("'", "''");
    }

    private static string SparkTypeName(string typeName)
    {
        return typeName.ToLowerInvariant() switch
        {
            "text" or "string" or "varchar" or "char" => "STRING",
            "int" or "integer" or "bigint" or "int64" or "long" => "BIGINT",
            "double" or "float" or "real" or "float64" => "DOUBLE",
            "boolean" or "bool" => "BOOLEAN",
            "bytes" or "bytea" or "blob" or "binary" => "BINARY",
            _ => typeName.ToUpperInvariant(),
        };
    }
}
