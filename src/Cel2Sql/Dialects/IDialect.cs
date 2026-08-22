using System.Text;

namespace Cel2Sql.Dialects;

/// <summary>
/// Defines the interface for SQL dialect-specific code generation.
/// The converter calls these methods at every point where SQL syntax diverges
/// between databases. Methods receive a <see cref="StringBuilder"/> that shares the
/// converter's output buffer, and callback functions (<see cref="SqlWriter"/>) for
/// writing sub-expressions.
/// </summary>
public interface IDialect
{
    /// <summary>The dialect name.</summary>
    DialectName Name { get; }

    // --- Literals ---

    /// <summary>Writes a string literal in the dialect's syntax.</summary>
    void WriteStringLiteral(StringBuilder w, string value);

    /// <summary>Writes a byte array literal in the dialect's syntax.</summary>
    void WriteBytesLiteral(StringBuilder w, byte[] value);

    /// <summary>Writes a parameter placeholder. PostgreSQL: $1; MySQL: ?; BigQuery: @p1.</summary>
    void WriteParamPlaceholder(StringBuilder w, int paramIndex);

    // --- Operators ---

    /// <summary>Writes a string concatenation expression.</summary>
    void WriteStringConcat(StringBuilder w, SqlWriter writeLhs, SqlWriter writeRhs);

    /// <summary>Writes a regex match expression.</summary>
    void WriteRegexMatch(StringBuilder w, SqlWriter writeTarget, string pattern, bool caseInsensitive);

    /// <summary>Writes the LIKE escape clause.</summary>
    void WriteLikeEscape(StringBuilder w);

    /// <summary>Writes an array membership test.</summary>
    void WriteArrayMembership(StringBuilder w, SqlWriter writeElem, SqlWriter writeArray);

    // --- Type Casting ---

    /// <summary>Writes a cast to numeric type.</summary>
    void WriteCastToNumeric(StringBuilder w);

    /// <summary>Writes a type name for CAST expressions.</summary>
    void WriteTypeName(StringBuilder w, string celTypeName);

    /// <summary>Writes extraction of epoch from a timestamp.</summary>
    void WriteEpochExtract(StringBuilder w, SqlWriter writeExpr);

    /// <summary>Writes a cast to timestamp type.</summary>
    void WriteTimestampCast(StringBuilder w, SqlWriter writeExpr);

    // --- Arrays ---

    /// <summary>Writes the opening of an array literal.</summary>
    void WriteArrayLiteralOpen(StringBuilder w);

    /// <summary>Writes the closing of an array literal.</summary>
    void WriteArrayLiteralClose(StringBuilder w);

    /// <summary>Writes an array length expression.</summary>
    void WriteArrayLength(StringBuilder w, int dimension, SqlWriter writeExpr);

    /// <summary>Writes a list index expression with dynamic index.</summary>
    void WriteListIndex(StringBuilder w, SqlWriter writeArray, SqlWriter writeIndex);

    /// <summary>Writes a constant list index (converts 0-indexed to dialect-appropriate).</summary>
    void WriteListIndexConst(StringBuilder w, SqlWriter writeArray, long index);

    /// <summary>Writes an empty typed array literal.</summary>
    void WriteEmptyTypedArray(StringBuilder w, string typeName);

    // --- JSON ---

    /// <summary>Writes JSON field access.</summary>
    void WriteJsonFieldAccess(StringBuilder w, SqlWriter writeBase, string fieldName, bool isFinal);

    /// <summary>Writes a JSON key existence check.</summary>
    void WriteJsonExistence(StringBuilder w, bool isJsonb, string fieldName, SqlWriter writeBase);

    /// <summary>Writes a call to extract JSON array elements.</summary>
    void WriteJsonArrayElements(StringBuilder w, bool isJsonb, bool asText, SqlWriter writeExpr);

    /// <summary>Writes a JSON array length expression.</summary>
    void WriteJsonArrayLength(StringBuilder w, SqlWriter writeExpr);

    /// <summary>Writes a JSON path extraction function.</summary>
    void WriteJsonExtractPath(StringBuilder w, IReadOnlyList<string> pathSegments, SqlWriter writeRoot);

    /// <summary>Writes a JSON array membership test for the IN operator.</summary>
    void WriteJsonArrayMembership(StringBuilder w, string jsonFunc, SqlWriter writeElem, SqlWriter writeArray);

    /// <summary>Writes a nested JSON array membership test.</summary>
    void WriteNestedJsonArrayMembership(StringBuilder w, SqlWriter writeElem, SqlWriter writeArray);

    // --- Timestamps ---

    /// <summary>Writes a duration/interval literal.</summary>
    void WriteDuration(StringBuilder w, long value, string unit);

    /// <summary>Writes an INTERVAL expression from a variable.</summary>
    void WriteInterval(StringBuilder w, SqlWriter writeValue, string unit);

    /// <summary>Writes a timestamp field extraction expression.</summary>
    void WriteExtract(StringBuilder w, string part, SqlWriter writeExpr, SqlWriter writeTz);

    /// <summary>Writes timestamp arithmetic.</summary>
    void WriteTimestampArithmetic(StringBuilder w, string op, SqlWriter writeTs, SqlWriter writeDur);

    // --- String Functions ---

    /// <summary>Writes a string contains expression.</summary>
    void WriteContains(StringBuilder w, SqlWriter writeHaystack, SqlWriter writeNeedle);

    /// <summary>Writes a string split expression.</summary>
    void WriteSplit(StringBuilder w, SqlWriter writeStr, SqlWriter writeDelim);

    /// <summary>Writes a string split expression with a limit.</summary>
    void WriteSplitWithLimit(StringBuilder w, SqlWriter writeStr, SqlWriter writeDelim, long limit);

    /// <summary>Writes an array join expression.</summary>
    void WriteJoin(StringBuilder w, SqlWriter writeArray, SqlWriter writeDelim);

    /// <summary>
    /// Writes a string format expression. <paramref name="formatSpec"/> is already validated
    /// against the accepted specifiers (%s, %d, %f). Implementations render it as a string
    /// literal then emit the argument writers comma-separated.
    /// </summary>
    void WriteFormat(StringBuilder w, string formatSpec, IReadOnlyList<SqlWriter> writeArgs);

    // --- Comprehensions ---

    /// <summary>Writes the UNNEST source for comprehensions.</summary>
    void WriteUnnest(StringBuilder w, SqlWriter writeSource);

    /// <summary>
    /// Writes the comprehension source (UNNEST/json_each + alias) for subqueries.
    /// Default implementation calls <see cref="WriteUnnest"/> and appends <c>AS iterVar</c>.
    /// </summary>
    void WriteComprehensionSource(StringBuilder w, SqlWriter writeSource, string iterVar);

    /// <summary>
    /// Wraps a comprehension's existential subquery: <c>EXISTS (SELECT 1 FROM &lt;body&gt;)</c>.
    /// MySQL overrides this with a COUNT comparison because its 8.x optimizer turns a
    /// correlated EXISTS into a semijoin and loses the correlation to a JSON_TABLE source,
    /// silently matching nothing.
    /// </summary>
    void WriteComprehensionExists(StringBuilder w, SqlWriter writeBody);

    /// <summary>Negation of <see cref="WriteComprehensionExists"/>: <c>NOT EXISTS (SELECT 1 FROM &lt;body&gt;)</c>.</summary>
    void WriteComprehensionNotExists(StringBuilder w, SqlWriter writeBody);

    /// <summary>Writes the prefix before the transform expression in an array-building subquery.</summary>
    void WriteArraySubqueryOpen(StringBuilder w);

    /// <summary>Writes the suffix after the transform expression and before FROM.</summary>
    void WriteArraySubqueryExprClose(StringBuilder w);

    // --- Struct ---

    /// <summary>Writes the opening of a struct/row literal.</summary>
    void WriteStructOpen(StringBuilder w);

    /// <summary>Writes the closing of a struct/row literal.</summary>
    void WriteStructClose(StringBuilder w);

    // --- Validation ---

    /// <summary>The maximum identifier length for this dialect.</summary>
    int MaxIdentifierLength { get; }

    /// <summary>Validates a field name for this dialect.</summary>
    void ValidateFieldName(string name);

    /// <summary>The set of reserved SQL keywords for this dialect.</summary>
    IReadOnlySet<string> ReservedKeywords { get; }

    // --- Regex ---

    /// <summary>Converts an RE2 regex pattern to the dialect's native format.</summary>
    RegexResult ConvertRegex(string re2Pattern);

    /// <summary>Whether this dialect supports regex matching.</summary>
    bool SupportsRegex { get; }

    // --- Capabilities ---

    /// <summary>Whether this dialect has native array types.</summary>
    bool SupportsNativeArrays { get; }

    /// <summary>Whether this dialect has a distinct JSONB type.</summary>
    bool SupportsJsonb { get; }

    /// <summary>Whether index analysis is supported.</summary>
    bool SupportsIndexAnalysis { get; }
}
