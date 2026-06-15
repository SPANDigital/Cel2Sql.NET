using System.Globalization;
using System.Text;
using Cel2Sql.Errors;

namespace Cel2Sql.Dialects.Sqlite;

/// <summary>
/// SQLite dialect implementation.
/// Implements the <see cref="IDialect"/> interface for SQLite-specific SQL generation.
///
/// <para>Ported from the Go <c>dialect/sqlite/dialect.go</c> implementation.</para>
/// </summary>
public sealed class SqliteDialect : DialectBase, IIndexAdvisor
{
    public SqliteDialect()
    {
    }

    public override DialectName Name => DialectName.Sqlite;

    // --- Literals ---

    public override void WriteBytesLiteral(StringBuilder w, byte[] value)
    {
        w.Append("X'");
        w.Append(Convert.ToHexString(value).ToLowerInvariant());
        w.Append('\'');
    }

    public override void WriteParamPlaceholder(StringBuilder w, int paramIndex)
    {
        w.Append('?');
    }

    // --- Operators ---

    public override void WriteRegexMatch(StringBuilder w, SqlWriter writeTarget, string pattern, bool caseInsensitive)
    {
        throw ConversionException.Of("Unsupported operation", "regex matching is not supported in SQLite");
    }

    public override void WriteLikeEscape(StringBuilder w)
    {
        w.Append(" ESCAPE '\\'");
    }

    public override void WriteArrayMembership(StringBuilder w, SqlWriter writeElem, SqlWriter writeArray)
    {
        writeElem();
        w.Append(" IN (SELECT value FROM json_each(");
        writeArray();
        w.Append("))");
    }

    // --- Type Casting ---

    public override void WriteCastToNumeric(StringBuilder w)
    {
        w.Append(" + 0");
    }

    public override void WriteTypeName(StringBuilder w, string celTypeName)
    {
        switch (celTypeName)
        {
            case "bool":
                w.Append("INTEGER");
                break;
            case "bytes":
                w.Append("BLOB");
                break;
            case "double":
                w.Append("REAL");
                break;
            case "int":
                w.Append("INTEGER");
                break;
            case "string":
                w.Append("TEXT");
                break;
            case "uint":
                w.Append("INTEGER");
                break;
            default:
                w.Append(celTypeName.ToUpperInvariant());
                break;
        }
    }

    public override void WriteEpochExtract(StringBuilder w, SqlWriter writeExpr)
    {
        w.Append("CAST(strftime('%s', ");
        writeExpr();
        w.Append(") AS INTEGER)");
    }

    public override void WriteTimestampCast(StringBuilder w, SqlWriter writeExpr)
    {
        w.Append("datetime(");
        writeExpr();
        w.Append(')');
    }

    // --- Arrays ---

    public override void WriteArrayLiteralOpen(StringBuilder w)
    {
        w.Append("json_array(");
    }

    public override void WriteArrayLiteralClose(StringBuilder w)
    {
        w.Append(')');
    }

    public override void WriteArrayLength(StringBuilder w, int dimension, SqlWriter writeExpr)
    {
        w.Append("COALESCE(json_array_length(");
        writeExpr();
        w.Append("), 0)");
    }

    public override void WriteListIndex(StringBuilder w, SqlWriter writeArray, SqlWriter writeIndex)
    {
        w.Append("json_extract(");
        writeArray();
        w.Append(", '$[' || ");
        writeIndex();
        w.Append(" || ']')");
    }

    public override void WriteListIndexConst(StringBuilder w, SqlWriter writeArray, long index)
    {
        w.Append("json_extract(");
        writeArray();
        w.Append(", '$[").Append(index).Append("]')");
    }

    public override void WriteEmptyTypedArray(StringBuilder w, string typeName)
    {
        w.Append("json_array()");
    }

    // --- JSON ---

    public override void WriteJsonFieldAccess(StringBuilder w, SqlWriter writeBase, string fieldName, bool isFinal)
    {
        string escapedField = EscapeJsonFieldName(fieldName);
        w.Append("json_extract(");
        writeBase();
        w.Append(", '$.").Append(escapedField).Append("')");
    }

    public override void WriteJsonExistence(StringBuilder w, bool isJsonb, string fieldName, SqlWriter writeBase)
    {
        string escapedField = EscapeJsonFieldName(fieldName);
        w.Append("json_type(");
        writeBase();
        w.Append(", '$.").Append(escapedField).Append("') IS NOT NULL");
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
        w.Append("json_type(");
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

    public override void WriteDuration(StringBuilder w, long value, string unit)
    {
        w.Append(string.Format(CultureInfo.InvariantCulture, "'{0:+0;-0} {1}s'", value, unit.ToLowerInvariant()));
    }

    public override void WriteInterval(StringBuilder w, SqlWriter writeValue, string unit)
    {
        w.Append("'+'||");
        writeValue();
        w.Append("||' ").Append(unit.ToLowerInvariant()).Append("s'");
    }

    public override void WriteExtract(StringBuilder w, string part, SqlWriter writeExpr, SqlWriter writeTz)
    {
        string format = part switch
        {
            "YEAR" => "%Y",
            "MONTH" => "%m",
            "DAY" => "%d",
            "HOUR" => "%H",
            "MINUTE" => "%M",
            "SECOND" => "%S",
            "DOY" => "%j",
            "DOW" => "%w",
            "MILLISECONDS" => "%f",
            _ => throw ConversionException.Of("Unsupported operation",
                "unsupported extract part: " + part),
        };
        w.Append("CAST(strftime('").Append(format).Append("', ");
        writeExpr();
        w.Append(") AS INTEGER)");
    }

    public override void WriteTimestampArithmetic(StringBuilder w, string op, SqlWriter writeTs, SqlWriter writeDur)
    {
        w.Append("datetime(");
        writeTs();
        w.Append(", ");
        if ("-".Equals(op, StringComparison.Ordinal))
        {
            w.Append("'-'||");
            writeDur();
        }
        else
        {
            writeDur();
        }
        w.Append(')');
    }

    // --- String Functions ---

    public override void WriteContains(StringBuilder w, SqlWriter writeHaystack, SqlWriter writeNeedle)
    {
        w.Append("INSTR(");
        writeHaystack();
        w.Append(", ");
        writeNeedle();
        w.Append(") > 0");
    }

    public override void WriteSplit(StringBuilder w, SqlWriter writeStr, SqlWriter writeDelim)
    {
        throw ConversionException.Of("Unsupported operation", "string split is not supported in SQLite");
    }

    public override void WriteSplitWithLimit(StringBuilder w, SqlWriter writeStr, SqlWriter writeDelim, long limit)
    {
        throw ConversionException.Of("Unsupported operation", "string split is not supported in SQLite");
    }

    public override void WriteJoin(StringBuilder w, SqlWriter writeArray, SqlWriter writeDelim)
    {
        throw ConversionException.Of("Unsupported operation", "array join is not supported in SQLite");
    }

    public override void WriteFormat(StringBuilder w, string formatSpec, IReadOnlyList<SqlWriter> writeArgs)
    {
        // SQLite's printf() supports C-style %s/%d/%f directly.
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
        w.Append("json_each(");
        writeSource();
        w.Append(')');
    }

    public override void WriteComprehensionSource(StringBuilder w, SqlWriter writeSource, string iterVar)
    {
        w.Append("(SELECT value AS ").Append(iterVar).Append(" FROM ");
        WriteUnnest(w, writeSource);
        w.Append(") AS _t");
    }

    public override void WriteArraySubqueryOpen(StringBuilder w)
    {
        w.Append("(SELECT json_group_array(");
    }

    public override void WriteArraySubqueryExprClose(StringBuilder w)
    {
        w.Append(')');
    }

    // --- Struct ---

    public override void WriteStructOpen(StringBuilder w)
    {
        w.Append("json_object(");
    }

    // --- Validation ---

    public override int MaxIdentifierLength => 0;

    public override void ValidateFieldName(string name)
    {
        SqliteValidation.ValidateFieldName(name);
    }

    public override IReadOnlySet<string> ReservedKeywords => SqliteValidation.GetReservedKeywords();

    // --- Regex ---

    public override RegexResult ConvertRegex(string re2Pattern)
    {
        throw ConversionException.Of("Unsupported operation", "regex is not supported in SQLite");
    }

    public override bool SupportsRegex => false;

    // --- Capabilities ---

    public override bool SupportsNativeArrays => false;

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
            PatternType.Comparison => new IndexRecommendation(col, "BTREE",
                string.Format(CultureInfo.InvariantCulture, "CREATE INDEX idx_{0} ON {1} ({2});", safeName, table, col),
                string.Format(CultureInfo.InvariantCulture, "Comparison operations on '{0}' benefit from a B-tree index for efficient range queries and equality checks", col)),
            _ => null,
        };
    }

    public IReadOnlyList<PatternType> SupportedPatterns()
    {
        return new[] { PatternType.Comparison };
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
