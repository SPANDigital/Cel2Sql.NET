using System.Globalization;
using System.Text;
using Cel2Sql.Errors;

namespace Cel2Sql.Dialects.DuckDb;

/// <summary>
/// DuckDB dialect implementation.
/// Implements the <see cref="IDialect"/> interface for DuckDB-specific SQL generation.
///
/// <para>Ported from the Go <c>dialect/duckdb/dialect.go</c> implementation.</para>
/// </summary>
public sealed class DuckDbDialect : DialectBase, IIndexAdvisor
{
    public DuckDbDialect()
    {
    }

    public override DialectName Name => DialectName.DuckDb;

    // --- Literals ---

    public override void WriteBytesLiteral(StringBuilder w, byte[] value)
    {
        w.Append("'\\x");
        w.Append(Convert.ToHexString(value).ToLowerInvariant());
        w.Append('\'');
    }

    public override void WriteParamPlaceholder(StringBuilder w, int paramIndex)
    {
        w.Append('$').Append(paramIndex);
    }

    // --- Operators ---

    public override void WriteRegexMatch(StringBuilder w, SqlWriter writeTarget, string pattern, bool caseInsensitive)
    {
        writeTarget();
        if (caseInsensitive)
        {
            w.Append(" ~* ");
        }
        else
        {
            w.Append(" ~ ");
        }
        string escaped = pattern.Replace("'", "''");
        w.Append('\'').Append(escaped).Append('\'');
    }

    public override void WriteLikeEscape(StringBuilder w)
    {
        w.Append(" ESCAPE '\\'");
    }

    public override void WriteArrayMembership(StringBuilder w, SqlWriter writeElem, SqlWriter writeArray)
    {
        writeElem();
        w.Append(" = ANY(");
        writeArray();
        w.Append(')');
    }

    // --- Type Casting ---

    public override void WriteCastToNumeric(StringBuilder w)
    {
        w.Append("::DOUBLE");
    }

    public override void WriteTypeName(StringBuilder w, string celTypeName)
    {
        switch (celTypeName)
        {
            case "bool":
                w.Append("BOOLEAN");
                break;
            case "bytes":
                w.Append("BLOB");
                break;
            case "double":
                w.Append("DOUBLE");
                break;
            case "int":
                w.Append("BIGINT");
                break;
            case "string":
                w.Append("VARCHAR");
                break;
            case "uint":
                w.Append("UBIGINT");
                break;
            default:
                w.Append(celTypeName.ToUpperInvariant());
                break;
        }
    }

    public override void WriteEpochExtract(StringBuilder w, SqlWriter writeExpr)
    {
        w.Append("EXTRACT(EPOCH FROM ");
        writeExpr();
        w.Append(")::BIGINT");
    }

    public override void WriteTimestampCast(StringBuilder w, SqlWriter writeExpr)
    {
        w.Append("CAST(");
        writeExpr();
        w.Append(" AS TIMESTAMPTZ)");
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
        w.Append("COALESCE(array_length(");
        writeExpr();
        w.Append("), 0)");
    }

    public override void WriteListIndex(StringBuilder w, SqlWriter writeArray, SqlWriter writeIndex)
    {
        writeArray();
        w.Append('[');
        writeIndex();
        w.Append(" + 1]");
    }

    public override void WriteListIndexConst(StringBuilder w, SqlWriter writeArray, long index)
    {
        writeArray();
        w.Append('[').Append(index + 1).Append(']');
    }

    public override void WriteEmptyTypedArray(StringBuilder w, string typeName)
    {
        w.Append("[]::").Append(typeName).Append("[]");
    }

    // --- JSON ---

    public override void WriteJsonFieldAccess(StringBuilder w, SqlWriter writeBase, string fieldName, bool isFinal)
    {
        writeBase();
        string escapedField = EscapeJsonFieldName(fieldName);
        if (isFinal)
        {
            w.Append("->>'");
        }
        else
        {
            w.Append("->'");
        }
        w.Append(escapedField).Append('\'');
    }

    public override void WriteJsonExistence(StringBuilder w, bool isJsonb, string fieldName, SqlWriter writeBase)
    {
        w.Append("json_exists(");
        writeBase();
        string escapedField = EscapeJsonFieldName(fieldName);
        w.Append(", '$.").Append(escapedField).Append("')");
    }

    public override void WriteJsonArrayElements(StringBuilder w, bool isJsonb, bool asText, SqlWriter writeExpr)
    {
        w.Append("json_each(");
        writeExpr();
        w.Append(')');
    }

    public override void WriteJsonArrayLength(StringBuilder w, SqlWriter writeExpr)
    {
        w.Append("COALESCE(json_array_length(");
        writeExpr();
        w.Append("), 0)");
    }

    public override void WriteJsonExtractPath(StringBuilder w, IReadOnlyList<string> pathSegments, SqlWriter writeRoot)
    {
        w.Append("json_exists(");
        writeRoot();
        w.Append(", '$");
        foreach (string segment in pathSegments)
        {
            w.Append('.').Append(EscapeJsonFieldName(segment));
        }
        w.Append("')");
    }

    public override void WriteJsonArrayMembership(StringBuilder w, string jsonFunc, SqlWriter writeElem, SqlWriter writeArray)
    {
        w.Append("EXISTS (SELECT 1 FROM json_each(");
        writeArray();
        w.Append(") WHERE value = ");
        writeElem();
        w.Append(')');
    }

    public override void WriteNestedJsonArrayMembership(StringBuilder w, SqlWriter writeElem, SqlWriter writeArray)
    {
        w.Append("EXISTS (SELECT 1 FROM json_each(");
        writeArray();
        w.Append(") WHERE value = ");
        writeElem();
        w.Append(')');
    }

    // --- Timestamps ---

    public override void WriteExtract(StringBuilder w, string part, SqlWriter writeExpr, SqlWriter writeTz)
    {
        bool isDow = "DOW".Equals(part, StringComparison.Ordinal);
        if (isDow)
        {
            w.Append('(');
        }
        w.Append("EXTRACT(").Append(part).Append(" FROM ");
        writeExpr();
        if (writeTz != null)
        {
            w.Append(" AT TIME ZONE ");
            writeTz();
        }
        w.Append(')');
        if (isDow)
        {
            w.Append(" + 6) % 7");
        }
    }

    // --- String Functions ---

    public override void WriteContains(StringBuilder w, SqlWriter writeHaystack, SqlWriter writeNeedle)
    {
        w.Append("CONTAINS(");
        writeHaystack();
        w.Append(", ");
        writeNeedle();
        w.Append(')');
    }

    public override void WriteSplit(StringBuilder w, SqlWriter writeStr, SqlWriter writeDelim)
    {
        w.Append("STRING_SPLIT(");
        writeStr();
        w.Append(", ");
        writeDelim();
        w.Append(')');
    }

    public override void WriteSplitWithLimit(StringBuilder w, SqlWriter writeStr, SqlWriter writeDelim, long limit)
    {
        w.Append("STRING_SPLIT(");
        writeStr();
        w.Append(", ");
        writeDelim();
        w.Append(")[1:").Append(limit).Append(']');
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
        // DuckDB's printf() supports C-style %s/%d/%f directly.
        w.Append("printf(");
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

    public override void WriteComprehensionSource(StringBuilder w, SqlWriter writeSource, string iterVar)
    {
        WriteUnnest(w, writeSource);
        w.Append(" AS _t(").Append(iterVar).Append(')');
    }

    public override void WriteArraySubqueryOpen(StringBuilder w)
    {
        w.Append("ARRAY(SELECT ");
    }

    public override void WriteArraySubqueryExprClose(StringBuilder w)
    {
        // No-op for DuckDB
    }

    // --- Struct ---

    public override void WriteStructOpen(StringBuilder w)
    {
        w.Append("ROW(");
    }

    // --- Validation ---

    public override int MaxIdentifierLength => DuckDbValidation.MaxIdentifierLength;

    public override void ValidateFieldName(string name)
    {
        DuckDbValidation.ValidateFieldName(name);
    }

    public override IReadOnlySet<string> ReservedKeywords => DuckDbValidation.GetReservedKeywords();

    // --- Regex ---

    public override RegexResult ConvertRegex(string re2Pattern)
    {
        return DuckDbRegex.ConvertRe2ToDuckDb(re2Pattern);
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
            PatternType.Comparison => new IndexRecommendation(col, "ART",
                string.Format(CultureInfo.InvariantCulture, "CREATE INDEX idx_{0} ON {1} ({2});", safeName, table, col),
                string.Format(CultureInfo.InvariantCulture, "Comparison operations on '{0}' benefit from an ART index for efficient range queries and equality checks", col)),
            PatternType.JsonAccess => new IndexRecommendation(col, "ART",
                string.Format(CultureInfo.InvariantCulture, "CREATE INDEX idx_{0}_json ON {1} ({2});", safeName, table, col),
                string.Format(CultureInfo.InvariantCulture, "JSON field access on '{0}' may benefit from an ART index", col)),
            PatternType.RegexMatch => null,
            PatternType.ArrayMembership => new IndexRecommendation(col, "ART",
                string.Format(CultureInfo.InvariantCulture, "CREATE INDEX idx_{0} ON {1} ({2});", safeName, table, col),
                string.Format(CultureInfo.InvariantCulture, "Array operations on '{0}' may benefit from an ART index", col)),
            PatternType.ArrayComprehension => new IndexRecommendation(col, "ART",
                string.Format(CultureInfo.InvariantCulture, "CREATE INDEX idx_{0} ON {1} ({2});", safeName, table, col),
                string.Format(CultureInfo.InvariantCulture, "Array operations on '{0}' may benefit from an ART index", col)),
            PatternType.JsonArrayComprehension => new IndexRecommendation(col, "ART",
                string.Format(CultureInfo.InvariantCulture, "CREATE INDEX idx_{0}_json ON {1} ({2});", safeName, table, col),
                string.Format(CultureInfo.InvariantCulture, "JSON array comprehension on '{0}' may benefit from an ART index", col)),
            _ => throw new ArgumentOutOfRangeException(nameof(pattern)),
        };
    }

    public IReadOnlyList<PatternType> SupportedPatterns()
    {
        return new[]
        {
            PatternType.Comparison, PatternType.JsonAccess, PatternType.ArrayMembership,
            PatternType.ArrayComprehension, PatternType.JsonArrayComprehension,
        };
    }

    // --- Internal helpers ---

    private static string EscapeJsonFieldName(string fieldName)
    {
        return fieldName.Replace("'", "''");
    }

    private static string SanitizeIndexName(string column)
    {
        string sanitized = column.Replace(".", "_").Replace(" ", "_").Replace("-", "_");
        return sanitized.Length > 50 ? sanitized.Substring(0, 50) : sanitized;
    }
}
