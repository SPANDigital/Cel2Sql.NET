using System.Globalization;
using System.Text;
using Cel2Sql.Errors;

namespace Cel2Sql.Dialects.MySql;

/// <summary>
/// MySQL dialect implementation.
/// Implements the <see cref="IDialect"/> interface for MySQL-specific SQL generation.
///
/// <para>Ported from the Go <c>dialect/mysql/dialect.go</c> implementation.</para>
/// </summary>
public sealed class MySqlDialect : DialectBase, IIndexAdvisor
{
    public MySqlDialect()
    {
    }

    public override DialectName Name => DialectName.MySql;

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

    public override void WriteStringConcat(StringBuilder w, SqlWriter writeLhs, SqlWriter writeRhs)
    {
        w.Append("CONCAT(");
        writeLhs();
        w.Append(", ");
        writeRhs();
        w.Append(')');
    }

    public override void WriteRegexMatch(StringBuilder w, SqlWriter writeTarget, string pattern, bool caseInsensitive)
    {
        writeTarget();
        w.Append(" REGEXP ");
        string escaped = pattern.Replace("'", "''");
        w.Append('\'').Append(escaped).Append('\'');
    }

    public override void WriteLikeEscape(StringBuilder w)
    {
        w.Append(" ESCAPE '\\\\'");
    }

    public override void WriteArrayMembership(StringBuilder w, SqlWriter writeElem, SqlWriter writeArray)
    {
        w.Append("JSON_OVERLAPS(JSON_ARRAY(");
        writeElem();
        w.Append("), ");
        writeArray();
        w.Append(')');
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
                w.Append("UNSIGNED");
                break;
            case "bytes":
                w.Append("BINARY");
                break;
            case "double":
                w.Append("DECIMAL");
                break;
            case "int":
                w.Append("SIGNED");
                break;
            case "string":
                w.Append("CHAR");
                break;
            case "uint":
                w.Append("UNSIGNED");
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
        w.Append(" AS DATETIME)");
    }

    // --- Arrays ---

    public override void WriteArrayLiteralOpen(StringBuilder w)
    {
        w.Append("JSON_ARRAY(");
    }

    public override void WriteArrayLiteralClose(StringBuilder w)
    {
        w.Append(')');
    }

    public override void WriteArrayLength(StringBuilder w, int dimension, SqlWriter writeExpr)
    {
        w.Append("COALESCE(JSON_LENGTH(");
        writeExpr();
        w.Append("), 0)");
    }

    public override void WriteListIndex(StringBuilder w, SqlWriter writeArray, SqlWriter writeIndex)
    {
        w.Append("JSON_EXTRACT(");
        writeArray();
        w.Append(", CONCAT('$[', ");
        writeIndex();
        w.Append(", ']'))");
    }

    public override void WriteListIndexConst(StringBuilder w, SqlWriter writeArray, long index)
    {
        w.Append("JSON_EXTRACT(");
        writeArray();
        w.Append(", '$[").Append(index).Append("]')");
    }

    public override void WriteEmptyTypedArray(StringBuilder w, string typeName)
    {
        w.Append("JSON_ARRAY()");
    }

    // --- JSON ---

    public override void WriteJsonFieldAccess(StringBuilder w, SqlWriter writeBase, string fieldName, bool isFinal)
    {
        writeBase();
        string escapedField = EscapeJsonFieldName(fieldName);
        if (isFinal)
        {
            w.Append("->>'$.").Append(escapedField).Append('\'');
        }
        else
        {
            w.Append("->'$.").Append(escapedField).Append('\'');
        }
    }

    public override void WriteJsonExistence(StringBuilder w, bool isJsonb, string fieldName, SqlWriter writeBase)
    {
        string escapedField = EscapeJsonFieldName(fieldName);
        w.Append("JSON_CONTAINS_PATH(");
        writeBase();
        w.Append(", 'one', '$.").Append(escapedField).Append("')");
    }

    public override void WriteJsonArrayElements(StringBuilder w, bool isJsonb, bool asText, SqlWriter writeExpr)
    {
        w.Append("JSON_TABLE(");
        writeExpr();
        w.Append(", '$[*]' COLUMNS(value TEXT PATH '$'))");
    }

    public override void WriteJsonArrayLength(StringBuilder w, SqlWriter writeExpr)
    {
        w.Append("COALESCE(JSON_LENGTH(");
        writeExpr();
        w.Append("), 0)");
    }

    public override void WriteJsonExtractPath(StringBuilder w, IReadOnlyList<string> pathSegments, SqlWriter writeRoot)
    {
        w.Append("JSON_CONTAINS_PATH(");
        writeRoot();
        w.Append(", 'one', '$");
        foreach (string segment in pathSegments)
        {
            w.Append('.').Append(EscapeJsonFieldName(segment));
        }
        w.Append("')");
    }

    public override void WriteJsonArrayMembership(StringBuilder w, string jsonFunc, SqlWriter writeElem, SqlWriter writeArray)
    {
        w.Append("JSON_OVERLAPS(JSON_ARRAY(");
        writeElem();
        w.Append("), ");
        writeArray();
        w.Append(')');
    }

    public override void WriteNestedJsonArrayMembership(StringBuilder w, SqlWriter writeElem, SqlWriter writeArray)
    {
        w.Append("JSON_OVERLAPS(JSON_ARRAY(");
        writeElem();
        w.Append("), ");
        writeArray();
        w.Append(')');
    }

    // --- Timestamps ---

    public override void WriteExtract(StringBuilder w, string part, SqlWriter writeExpr, SqlWriter writeTz)
    {
        bool isDow = "DOW".Equals(part, StringComparison.Ordinal);
        if (isDow)
        {
            w.Append("(DAYOFWEEK(");
            writeExpr();
            if (writeTz != null)
            {
                w.Append(" AT TIME ZONE ");
                writeTz();
            }
            w.Append(") + 5) % 7");
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

    // --- String Functions ---

    public override void WriteContains(StringBuilder w, SqlWriter writeHaystack, SqlWriter writeNeedle)
    {
        w.Append("LOCATE(");
        writeNeedle();
        w.Append(", ");
        writeHaystack();
        w.Append(") > 0");
    }

    public override void WriteSplit(StringBuilder w, SqlWriter writeStr, SqlWriter writeDelim)
    {
        w.Append("JSON_ARRAY(");
        writeStr();
        w.Append(')');
    }

    public override void WriteSplitWithLimit(StringBuilder w, SqlWriter writeStr, SqlWriter writeDelim, long limit)
    {
        WriteSplit(w, writeStr, writeDelim);
    }

    public override void WriteJoin(StringBuilder w, SqlWriter writeArray, SqlWriter writeDelim)
    {
        w.Append("JSON_UNQUOTE(");
        writeArray();
        w.Append(')');
    }

    public override void WriteFormat(StringBuilder w, string formatSpec, IReadOnlyList<SqlWriter> writeArgs)
    {
        // MySQL has no printf-style FORMAT(); FORMAT(N, decimals) formats numbers only.
        // Rather than emit incorrect SQL, fail explicitly so callers know to handle
        // the formatting in application code or pick a different dialect.
        throw ConversionException.Of(
            "Unsupported operation",
            "format() is not supported in MySQL: MySQL has no printf-style FORMAT function");
    }

    // --- Comprehensions ---

    public override void WriteUnnest(StringBuilder w, SqlWriter writeSource)
    {
        w.Append("JSON_TABLE(");
        writeSource();
        w.Append(", '$[*]' COLUMNS(value TEXT PATH '$'))");
    }

    public override void WriteArraySubqueryOpen(StringBuilder w)
    {
        w.Append("(SELECT JSON_ARRAYAGG(");
    }

    public override void WriteArraySubqueryExprClose(StringBuilder w)
    {
        w.Append(')');
    }

    // --- Struct ---

    public override void WriteStructOpen(StringBuilder w)
    {
        w.Append("ROW(");
    }

    // --- Validation ---

    public override int MaxIdentifierLength => MySqlValidation.MaxIdentifierLength;

    public override void ValidateFieldName(string name)
    {
        MySqlValidation.ValidateFieldName(name);
    }

    public override IReadOnlySet<string> ReservedKeywords => MySqlValidation.GetReservedKeywords();

    // --- Regex ---

    public override RegexResult ConvertRegex(string re2Pattern)
    {
        return MySqlRegex.ConvertRe2ToMySql(re2Pattern);
    }

    public override bool SupportsRegex => true;

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
            PatternType.JsonAccess => new IndexRecommendation(col, "BTREE",
                string.Format(CultureInfo.InvariantCulture, "CREATE INDEX idx_{0}_json ON {1} ((CAST({2}->>'$.key' AS CHAR(255))));", safeName, table, col),
                string.Format(CultureInfo.InvariantCulture, "JSON field access on '{0}' benefits from a functional B-tree index", col)),
            PatternType.RegexMatch => new IndexRecommendation(col, "FULLTEXT",
                string.Format(CultureInfo.InvariantCulture, "CREATE FULLTEXT INDEX idx_{0}_ft ON {1} ({2});", safeName, table, col),
                string.Format(CultureInfo.InvariantCulture, "Regex matching on '{0}' may benefit from a FULLTEXT index for pattern matching", col)),
            PatternType.ArrayMembership => null,
            PatternType.ArrayComprehension => null,
            PatternType.JsonArrayComprehension => new IndexRecommendation(col, "BTREE",
                string.Format(CultureInfo.InvariantCulture, "CREATE INDEX idx_{0}_json ON {1} ((CAST({2}->>'$.key' AS CHAR(255))));", safeName, table, col),
                string.Format(CultureInfo.InvariantCulture, "JSON array comprehension on '{0}' may benefit from a functional B-tree index", col)),
            _ => throw new ArgumentOutOfRangeException(nameof(pattern)),
        };
    }

    public IReadOnlyList<PatternType> SupportedPatterns()
    {
        return new[]
        {
            PatternType.Comparison, PatternType.JsonAccess, PatternType.RegexMatch,
            PatternType.JsonArrayComprehension,
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
