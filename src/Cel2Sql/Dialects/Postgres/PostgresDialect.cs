using System.Globalization;
using System.Text;
using Cel2Sql.Errors;

namespace Cel2Sql.Dialects.Postgres;

/// <summary>
/// PostgreSQL dialect implementation.
/// Implements the <see cref="IDialect"/> interface for PostgreSQL-specific SQL generation.
///
/// <para>Ported from the Go <c>dialect/postgres/postgres.go</c> implementation.</para>
/// </summary>
public sealed class PostgresDialect : DialectBase, IIndexAdvisor
{
    public PostgresDialect()
    {
    }

    public override DialectName Name => DialectName.PostgreSql;

    // --- Literals ---

    public override void WriteStringLiteral(StringBuilder w, string value)
    {
        string escaped = value.Replace("'", "''");
        w.Append('\'').Append(escaped).Append('\'');
    }

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

    public override void WriteStringConcat(StringBuilder w, SqlWriter writeLhs, SqlWriter writeRhs)
    {
        writeLhs();
        w.Append(" || ");
        writeRhs();
    }

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
        w.Append(" ESCAPE E'\\\\'");
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
        w.Append("::numeric");
    }

    public override void WriteTypeName(StringBuilder w, string celTypeName)
    {
        switch (celTypeName)
        {
            case "bool":
                w.Append("BOOLEAN");
                break;
            case "bytes":
                w.Append("BYTEA");
                break;
            case "double":
                w.Append("DOUBLE PRECISION");
                break;
            case "int":
                w.Append("BIGINT");
                break;
            case "string":
                w.Append("TEXT");
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
        w.Append("EXTRACT(EPOCH FROM ");
        writeExpr();
        w.Append(")::bigint");
    }

    public override void WriteTimestampCast(StringBuilder w, SqlWriter writeExpr)
    {
        w.Append("CAST(");
        writeExpr();
        w.Append(" AS TIMESTAMP WITH TIME ZONE)");
    }

    // --- Arrays ---

    public override void WriteArrayLiteralOpen(StringBuilder w)
    {
        w.Append("ARRAY[");
    }

    public override void WriteArrayLiteralClose(StringBuilder w)
    {
        w.Append(']');
    }

    public override void WriteArrayLength(StringBuilder w, int dimension, SqlWriter writeExpr)
    {
        w.Append("COALESCE(ARRAY_LENGTH(");
        writeExpr();
        w.Append(", ").Append(dimension).Append("), 0)");
    }

    public override void WriteListIndex(StringBuilder w, SqlWriter writeArray, SqlWriter writeIndex)
    {
        w.Append('(');
        writeArray();
        w.Append(")[");
        writeIndex();
        w.Append(" + 1]");
    }

    public override void WriteListIndexConst(StringBuilder w, SqlWriter writeArray, long index)
    {
        w.Append('(');
        writeArray();
        w.Append(")[").Append(index + 1).Append(']');
    }

    public override void WriteEmptyTypedArray(StringBuilder w, string typeName)
    {
        w.Append("ARRAY[]::").Append(typeName).Append("[]");
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
        writeBase();
        string escapedField = EscapeJsonFieldName(fieldName);
        if (isJsonb)
        {
            w.Append(" ? '").Append(escapedField).Append('\'');
        }
        else
        {
            w.Append("->'").Append(escapedField).Append("' IS NOT NULL");
        }
    }

    public override void WriteJsonArrayElements(StringBuilder w, bool isJsonb, bool asText, SqlWriter writeExpr)
    {
        if (isJsonb)
        {
            w.Append(asText ? "jsonb_array_elements_text(" : "jsonb_array_elements(");
        }
        else
        {
            w.Append(asText ? "json_array_elements_text(" : "json_array_elements(");
        }
        writeExpr();
        w.Append(')');
    }

    public override void WriteJsonArrayLength(StringBuilder w, SqlWriter writeExpr)
    {
        w.Append("COALESCE(jsonb_array_length(");
        writeExpr();
        w.Append("), 0)");
    }

    public override void WriteJsonExtractPath(StringBuilder w, IReadOnlyList<string> pathSegments, SqlWriter writeRoot)
    {
        w.Append("jsonb_extract_path_text(");
        writeRoot();
        foreach (string segment in pathSegments)
        {
            w.Append(", '").Append(EscapeJsonFieldName(segment)).Append('\'');
        }
        w.Append(") IS NOT NULL");
    }

    public override void WriteJsonArrayMembership(StringBuilder w, string jsonFunc, SqlWriter writeElem, SqlWriter writeArray)
    {
        writeElem();
        w.Append(" = ANY(ARRAY(SELECT ").Append(jsonFunc).Append('(');
        writeArray();
        w.Append(")))");
    }

    public override void WriteNestedJsonArrayMembership(StringBuilder w, SqlWriter writeElem, SqlWriter writeArray)
    {
        writeElem();
        w.Append(" = ANY(ARRAY(SELECT jsonb_array_elements_text(");
        writeArray();
        w.Append(")))");
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

    public override void WriteTimestampArithmetic(StringBuilder w, string op, SqlWriter writeTs, SqlWriter writeDur)
    {
        writeTs();
        w.Append(' ').Append(op).Append(' ');
        writeDur();
    }

    // --- String Functions ---

    public override void WriteContains(StringBuilder w, SqlWriter writeHaystack, SqlWriter writeNeedle)
    {
        w.Append("POSITION(");
        writeNeedle();
        w.Append(" IN ");
        writeHaystack();
        w.Append(") > 0");
    }

    public override void WriteSplit(StringBuilder w, SqlWriter writeStr, SqlWriter writeDelim)
    {
        w.Append("STRING_TO_ARRAY(");
        writeStr();
        w.Append(", ");
        writeDelim();
        w.Append(')');
    }

    public override void WriteSplitWithLimit(StringBuilder w, SqlWriter writeStr, SqlWriter writeDelim, long limit)
    {
        w.Append("(STRING_TO_ARRAY(");
        writeStr();
        w.Append(", ");
        writeDelim();
        w.Append("))[1:").Append(limit).Append(']');
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
        w.Append(", '')");
    }

    public override void WriteFormat(StringBuilder w, string formatSpec, IReadOnlyList<SqlWriter> writeArgs)
    {
        // PostgreSQL's FORMAT() supports only %s (and %I/%L for identifiers/literals).
        // Coerce CEL's %d/%f to %s so numeric args print correctly via implicit casting.
        string pgSpec = System.Text.RegularExpressions.Regex.Replace(formatSpec, "%[df]", "%s");
        w.Append("FORMAT(");
        WriteStringLiteral(w, pgSpec);
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
        // No-op for PostgreSQL
    }

    // --- Struct ---

    public override void WriteStructOpen(StringBuilder w)
    {
        w.Append("ROW(");
    }

    public override void WriteStructClose(StringBuilder w)
    {
        w.Append(')');
    }

    // --- Validation ---

    public override int MaxIdentifierLength => PostgresValidation.MaxIdentifierLength;

    public override void ValidateFieldName(string name)
    {
        PostgresValidation.ValidateFieldName(name);
    }

    public override IReadOnlySet<string> ReservedKeywords => PostgresValidation.GetReservedKeywords();

    // --- Regex ---

    public override RegexResult ConvertRegex(string re2Pattern)
    {
        return PostgresRegex.ConvertRe2ToPosix(re2Pattern);
    }

    public override bool SupportsRegex => true;

    // --- Capabilities ---

    public override bool SupportsNativeArrays => true;

    public override bool SupportsJsonb => true;

    public override bool SupportsIndexAnalysis => true;

    // --- Index Advisor ---

    public IndexRecommendation? RecommendIndex(IndexPattern pattern)
    {
        string table = !string.IsNullOrEmpty(pattern.TableHint) ? pattern.TableHint! : "table_name";
        string col = pattern.Column;
        string safeName = SanitizeIndexName(col);

        return pattern.Pattern switch
        {
            PatternType.Comparison => new IndexRecommendation(col, "BTREE",
                string.Format(CultureInfo.InvariantCulture, "CREATE INDEX idx_{0}_btree ON {1} ({2});", safeName, table, col),
                string.Format(CultureInfo.InvariantCulture, "Comparison operations on '{0}' benefit from B-tree index for efficient range queries and equality checks", col)),
            PatternType.JsonAccess => new IndexRecommendation(col, "GIN",
                string.Format(CultureInfo.InvariantCulture, "CREATE INDEX idx_{0}_gin ON {1} USING GIN ({2});", safeName, table, col),
                string.Format(CultureInfo.InvariantCulture, "JSON path operations on '{0}' benefit from GIN index for efficient nested field access", col)),
            PatternType.RegexMatch => new IndexRecommendation(col, "GIN",
                string.Format(CultureInfo.InvariantCulture, "CREATE INDEX idx_{0}_gin_trgm ON {1} USING GIN ({2} gin_trgm_ops);", safeName, table, col),
                string.Format(CultureInfo.InvariantCulture, "Regex matching on '{0}' benefits from GIN index with pg_trgm extension for pattern matching", col)),
            PatternType.ArrayMembership => new IndexRecommendation(col, "GIN",
                string.Format(CultureInfo.InvariantCulture, "CREATE INDEX idx_{0}_gin ON {1} USING GIN ({2});", safeName, table, col),
                string.Format(CultureInfo.InvariantCulture, "Array membership tests on '{0}' benefit from GIN index for efficient element lookups", col)),
            PatternType.ArrayComprehension => new IndexRecommendation(col, "GIN",
                string.Format(CultureInfo.InvariantCulture, "CREATE INDEX idx_{0}_gin ON {1} USING GIN ({2});", safeName, table, col),
                string.Format(CultureInfo.InvariantCulture, "Array comprehension on '{0}' benefits from GIN index for efficient array operations", col)),
            PatternType.JsonArrayComprehension => new IndexRecommendation(col, "GIN",
                string.Format(CultureInfo.InvariantCulture, "CREATE INDEX idx_{0}_gin ON {1} USING GIN ({2});", safeName, table, col),
                string.Format(CultureInfo.InvariantCulture, "JSONB array comprehension on '{0}' benefits from GIN index for efficient array element access", col)),
            _ => throw new ArgumentOutOfRangeException(nameof(pattern)),
        };
    }

    public IReadOnlyList<PatternType> SupportedPatterns()
    {
        return new[]
        {
            PatternType.Comparison, PatternType.JsonAccess, PatternType.RegexMatch,
            PatternType.ArrayMembership, PatternType.ArrayComprehension, PatternType.JsonArrayComprehension,
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
