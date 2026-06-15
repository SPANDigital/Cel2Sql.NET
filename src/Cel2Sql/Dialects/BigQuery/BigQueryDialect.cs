using System.Globalization;
using System.Text;
using Cel2Sql.Errors;

namespace Cel2Sql.Dialects.BigQuery;

/// <summary>
/// BigQuery dialect implementation.
/// Implements the <see cref="IDialect"/> interface for BigQuery-specific SQL generation.
///
/// <para>Ported from the Go <c>dialect/bigquery/dialect.go</c> implementation.</para>
/// </summary>
public sealed class BigQueryDialect : DialectBase, IIndexAdvisor
{
    public BigQueryDialect()
    {
    }

    public override DialectName Name => DialectName.BigQuery;

    // --- Literals ---

    public override void WriteStringLiteral(StringBuilder w, string value)
    {
        string escaped = value.Replace("'", "\\'");
        w.Append('\'').Append(escaped).Append('\'');
    }

    public override void WriteBytesLiteral(StringBuilder w, byte[] value)
    {
        w.Append("b\"");
        foreach (byte b in value)
        {
            w.Append(string.Format(CultureInfo.InvariantCulture, "\\{0}", Convert.ToString(b & 0xFF, 8).PadLeft(3, '0')));
        }
        w.Append('"');
    }

    public override void WriteParamPlaceholder(StringBuilder w, int paramIndex)
    {
        w.Append("@p").Append(paramIndex);
    }

    // --- Operators ---

    public override void WriteRegexMatch(StringBuilder w, SqlWriter writeTarget, string pattern, bool caseInsensitive)
    {
        w.Append("REGEXP_CONTAINS(");
        writeTarget();
        w.Append(", '");
        string escaped = pattern.Replace("'", "\\'");
        w.Append(escaped);
        w.Append("')");
    }

    public override void WriteLikeEscape(StringBuilder w)
    {
        // No-op: BigQuery uses backslash as the default escape character, no ESCAPE keyword needed
    }

    public override void WriteArrayMembership(StringBuilder w, SqlWriter writeElem, SqlWriter writeArray)
    {
        writeElem();
        w.Append(" IN UNNEST(");
        writeArray();
        w.Append(')');
    }

    // --- Type Casting ---

    public override void WriteCastToNumeric(StringBuilder w)
    {
        w.Append("::FLOAT64");
    }

    public override void WriteTypeName(StringBuilder w, string celTypeName)
    {
        switch (celTypeName)
        {
            case "bool":
                w.Append("BOOL");
                break;
            case "bytes":
                w.Append("BYTES");
                break;
            case "double":
                w.Append("FLOAT64");
                break;
            case "int":
                w.Append("INT64");
                break;
            case "string":
                w.Append("STRING");
                break;
            case "uint":
                w.Append("INT64");
                break;
            default:
                w.Append(celTypeName.ToUpperInvariant());
                break;
        }
    }

    public override void WriteEpochExtract(StringBuilder w, SqlWriter writeExpr)
    {
        w.Append("UNIX_SECONDS(");
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
        w.Append('[');
    }

    public override void WriteArrayLiteralClose(StringBuilder w)
    {
        w.Append(']');
    }

    public override void WriteArrayLength(StringBuilder w, int dimension, SqlWriter writeExpr)
    {
        // Wrap in COALESCE so size(arr) > 0 correctly excludes NULL arrays
        // (matches PostgreSQL/MySQL/SQLite/DuckDB behavior).
        w.Append("COALESCE(ARRAY_LENGTH(");
        writeExpr();
        w.Append("), 0)");
    }

    public override void WriteListIndex(StringBuilder w, SqlWriter writeArray, SqlWriter writeIndex)
    {
        writeArray();
        w.Append("[OFFSET(");
        writeIndex();
        w.Append(")]");
    }

    public override void WriteListIndexConst(StringBuilder w, SqlWriter writeArray, long index)
    {
        writeArray();
        w.Append("[OFFSET(").Append(index).Append(")]");
    }

    public override void WriteEmptyTypedArray(StringBuilder w, string typeName)
    {
        w.Append("ARRAY<").Append(BigQueryTypeName(typeName)).Append(">[]");
    }

    // --- JSON ---

    public override void WriteJsonFieldAccess(StringBuilder w, SqlWriter writeBase, string fieldName, bool isFinal)
    {
        string escapedField = EscapeJsonFieldName(fieldName);
        if (isFinal)
        {
            w.Append("JSON_VALUE(");
        }
        else
        {
            w.Append("JSON_QUERY(");
        }
        writeBase();
        w.Append(", '$.").Append(escapedField).Append("')");
    }

    public override void WriteJsonExistence(StringBuilder w, bool isJsonb, string fieldName, SqlWriter writeBase)
    {
        string escapedField = EscapeJsonFieldName(fieldName);
        w.Append("JSON_VALUE(");
        writeBase();
        w.Append(", '$.").Append(escapedField).Append("') IS NOT NULL");
    }

    public override void WriteJsonArrayElements(StringBuilder w, bool isJsonb, bool asText, SqlWriter writeExpr)
    {
        w.Append("UNNEST(JSON_QUERY_ARRAY(");
        writeExpr();
        w.Append("))");
    }

    public override void WriteJsonArrayLength(StringBuilder w, SqlWriter writeExpr)
    {
        w.Append("COALESCE(ARRAY_LENGTH(JSON_QUERY_ARRAY(");
        writeExpr();
        w.Append(")), 0)");
    }

    public override void WriteJsonExtractPath(StringBuilder w, IReadOnlyList<string> pathSegments, SqlWriter writeRoot)
    {
        w.Append("JSON_VALUE(");
        writeRoot();
        w.Append(", '$");
        foreach (string segment in pathSegments)
        {
            w.Append('.').Append(EscapeJsonFieldName(segment));
        }
        w.Append("') IS NOT NULL");
    }

    public override void WriteJsonArrayMembership(StringBuilder w, string jsonFunc, SqlWriter writeElem, SqlWriter writeArray)
    {
        writeElem();
        w.Append(" IN UNNEST(JSON_VALUE_ARRAY(");
        writeArray();
        w.Append("))");
    }

    public override void WriteNestedJsonArrayMembership(StringBuilder w, SqlWriter writeElem, SqlWriter writeArray)
    {
        writeElem();
        w.Append(" IN UNNEST(JSON_VALUE_ARRAY(");
        writeArray();
        w.Append("))");
    }

    // --- Timestamps ---

    public override void WriteExtract(StringBuilder w, string part, SqlWriter writeExpr, SqlWriter writeTz)
    {
        bool isDow = "DOW".Equals(part, StringComparison.Ordinal);
        if (isDow)
        {
            w.Append("(EXTRACT(DAYOFWEEK FROM ");
            writeExpr();
            if (writeTz != null)
            {
                w.Append(" AT TIME ZONE ");
                writeTz();
            }
            w.Append(") - 1)");
        }
        else
        {
            w.Append("EXTRACT(").Append(part).Append(" FROM ");
            writeExpr();
            if (writeTz != null)
            {
                w.Append(" AT TIME ZONE ");
                writeTz();
            }
            w.Append(')');
        }
    }

    public override void WriteTimestampArithmetic(StringBuilder w, string op, SqlWriter writeTs, SqlWriter writeDur)
    {
        if ("+".Equals(op, StringComparison.Ordinal))
        {
            w.Append("TIMESTAMP_ADD(");
        }
        else
        {
            w.Append("TIMESTAMP_SUB(");
        }
        writeTs();
        w.Append(", ");
        writeDur();
        w.Append(')');
    }

    // --- String Functions ---

    public override void WriteContains(StringBuilder w, SqlWriter writeHaystack, SqlWriter writeNeedle)
    {
        w.Append("STRPOS(");
        writeHaystack();
        w.Append(", ");
        writeNeedle();
        w.Append(") > 0");
    }

    public override void WriteSplit(StringBuilder w, SqlWriter writeStr, SqlWriter writeDelim)
    {
        w.Append("SPLIT(");
        writeStr();
        w.Append(", ");
        writeDelim();
        w.Append(')');
    }

    public override void WriteSplitWithLimit(StringBuilder w, SqlWriter writeStr, SqlWriter writeDelim, long limit)
    {
        w.Append("ARRAY(SELECT x FROM UNNEST(SPLIT(");
        writeStr();
        w.Append(", ");
        writeDelim();
        w.Append(")) AS x WITH OFFSET WHERE OFFSET < ").Append(limit).Append(')');
    }

    public override void WriteJoin(StringBuilder w, SqlWriter writeArray, SqlWriter writeDelim)
    {
        w.Append("ARRAY_TO_STRING(");
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
        // BigQuery FORMAT() accepts C-style %s, %d, %f — pass through unchanged.
        w.Append("FORMAT(");
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
        w.Append("UNNEST(");
        writeSource();
        w.Append(')');
    }

    public override void WriteArraySubqueryOpen(StringBuilder w)
    {
        w.Append("ARRAY(SELECT ");
    }

    public override void WriteArraySubqueryExprClose(StringBuilder w)
    {
        // No-op for BigQuery
    }

    // --- Struct ---

    public override void WriteStructOpen(StringBuilder w)
    {
        w.Append("STRUCT(");
    }

    // --- Validation ---

    public override int MaxIdentifierLength => BigQueryValidation.MaxIdentifierLength;

    public override void ValidateFieldName(string name)
    {
        BigQueryValidation.ValidateFieldName(name);
    }

    public override IReadOnlySet<string> ReservedKeywords => BigQueryValidation.GetReservedKeywords();

    // --- Regex ---

    public override RegexResult ConvertRegex(string re2Pattern)
    {
        return BigQueryRegex.ConvertRe2ToBigQuery(re2Pattern);
    }

    public override bool SupportsRegex => true;

    // --- Capabilities ---

    public override bool SupportsNativeArrays => true;

    public override bool SupportsJsonb => false;

    public override bool SupportsIndexAnalysis => true;

    // --- Index Advisor ---

    public IndexRecommendation? RecommendIndex(IndexPattern pattern)
    {
        string table = !string.IsNullOrEmpty(pattern.TableHint) ? pattern.TableHint! : "table_name";
        string col = pattern.Column;
        string safeName = SanitizeIndexName(col);

        return pattern.Pattern switch
        {
            PatternType.Comparison => new IndexRecommendation(col, "CLUSTERING",
                string.Format(CultureInfo.InvariantCulture, "-- Add '{0}' as a clustering key on {1}", col, table),
                string.Format(CultureInfo.InvariantCulture, "Comparison operations on '{0}' benefit from clustering for efficient range queries", col)),
            PatternType.JsonAccess => new IndexRecommendation(col, "SEARCH_INDEX",
                string.Format(CultureInfo.InvariantCulture, "CREATE SEARCH INDEX idx_{0} ON {1} ({2});", safeName, table, col),
                string.Format(CultureInfo.InvariantCulture, "JSON field access on '{0}' benefits from a search index", col)),
            PatternType.RegexMatch => null,
            PatternType.ArrayMembership => null,
            PatternType.ArrayComprehension => null,
            PatternType.JsonArrayComprehension => new IndexRecommendation(col, "SEARCH_INDEX",
                string.Format(CultureInfo.InvariantCulture, "CREATE SEARCH INDEX idx_{0} ON {1} ({2});", safeName, table, col),
                string.Format(CultureInfo.InvariantCulture, "JSON array comprehension on '{0}' benefits from a search index", col)),
            _ => null,
        };
    }

    public IReadOnlyList<PatternType> SupportedPatterns()
    {
        return new[]
        {
            PatternType.Comparison, PatternType.JsonAccess, PatternType.JsonArrayComprehension,
        };
    }

    // --- Internal helpers ---

    private static string EscapeJsonFieldName(string fieldName)
    {
        return fieldName.Replace("'", "\\'");
    }

    private static string SanitizeIndexName(string column)
    {
        string sanitized = column.Replace(".", "_").Replace(" ", "_").Replace("-", "_");
        return sanitized.Length > 50 ? sanitized.Substring(0, 50) : sanitized;
    }

    private static string BigQueryTypeName(string typeName)
    {
        return typeName.ToLowerInvariant() switch
        {
            "text" or "string" or "varchar" => "STRING",
            "int" or "integer" or "bigint" or "int64" => "INT64",
            "double" or "float" or "real" or "float64" => "FLOAT64",
            "boolean" or "bool" => "BOOL",
            "bytes" or "bytea" or "blob" => "BYTES",
            _ => typeName.ToUpperInvariant(),
        };
    }
}
