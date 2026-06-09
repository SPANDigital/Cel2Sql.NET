using System.Text;

namespace Cel2Sql.Dialects;

/// <summary>
/// Base class for dialect implementations. Provides the default
/// <see cref="WriteComprehensionSource"/> behaviour (the Java interface default method);
/// dialects that need different aliasing override it.
/// </summary>
public abstract class DialectBase : IDialect
{
    public abstract DialectName Name { get; }

    public abstract void WriteStringLiteral(StringBuilder w, string value);
    public abstract void WriteBytesLiteral(StringBuilder w, byte[] value);
    public abstract void WriteParamPlaceholder(StringBuilder w, int paramIndex);
    public abstract void WriteStringConcat(StringBuilder w, SqlWriter writeLhs, SqlWriter writeRhs);
    public abstract void WriteRegexMatch(StringBuilder w, SqlWriter writeTarget, string pattern, bool caseInsensitive);
    public abstract void WriteLikeEscape(StringBuilder w);
    public abstract void WriteArrayMembership(StringBuilder w, SqlWriter writeElem, SqlWriter writeArray);
    public abstract void WriteCastToNumeric(StringBuilder w);
    public abstract void WriteTypeName(StringBuilder w, string celTypeName);
    public abstract void WriteEpochExtract(StringBuilder w, SqlWriter writeExpr);
    public abstract void WriteTimestampCast(StringBuilder w, SqlWriter writeExpr);
    public abstract void WriteArrayLiteralOpen(StringBuilder w);
    public abstract void WriteArrayLiteralClose(StringBuilder w);
    public abstract void WriteArrayLength(StringBuilder w, int dimension, SqlWriter writeExpr);
    public abstract void WriteListIndex(StringBuilder w, SqlWriter writeArray, SqlWriter writeIndex);
    public abstract void WriteListIndexConst(StringBuilder w, SqlWriter writeArray, long index);
    public abstract void WriteEmptyTypedArray(StringBuilder w, string typeName);
    public abstract void WriteJsonFieldAccess(StringBuilder w, SqlWriter writeBase, string fieldName, bool isFinal);
    public abstract void WriteJsonExistence(StringBuilder w, bool isJsonb, string fieldName, SqlWriter writeBase);
    public abstract void WriteJsonArrayElements(StringBuilder w, bool isJsonb, bool asText, SqlWriter writeExpr);
    public abstract void WriteJsonArrayLength(StringBuilder w, SqlWriter writeExpr);
    public abstract void WriteJsonExtractPath(StringBuilder w, IReadOnlyList<string> pathSegments, SqlWriter writeRoot);
    public abstract void WriteJsonArrayMembership(StringBuilder w, string jsonFunc, SqlWriter writeElem, SqlWriter writeArray);
    public abstract void WriteNestedJsonArrayMembership(StringBuilder w, SqlWriter writeElem, SqlWriter writeArray);
    public abstract void WriteDuration(StringBuilder w, long value, string unit);
    public abstract void WriteInterval(StringBuilder w, SqlWriter writeValue, string unit);
    public abstract void WriteExtract(StringBuilder w, string part, SqlWriter writeExpr, SqlWriter writeTz);
    public abstract void WriteTimestampArithmetic(StringBuilder w, string op, SqlWriter writeTs, SqlWriter writeDur);
    public abstract void WriteContains(StringBuilder w, SqlWriter writeHaystack, SqlWriter writeNeedle);
    public abstract void WriteSplit(StringBuilder w, SqlWriter writeStr, SqlWriter writeDelim);
    public abstract void WriteSplitWithLimit(StringBuilder w, SqlWriter writeStr, SqlWriter writeDelim, long limit);
    public abstract void WriteJoin(StringBuilder w, SqlWriter writeArray, SqlWriter writeDelim);
    public abstract void WriteFormat(StringBuilder w, string formatSpec, IReadOnlyList<SqlWriter> writeArgs);
    public abstract void WriteUnnest(StringBuilder w, SqlWriter writeSource);
    public abstract void WriteArraySubqueryOpen(StringBuilder w);
    public abstract void WriteArraySubqueryExprClose(StringBuilder w);
    public abstract void WriteStructOpen(StringBuilder w);
    public abstract void WriteStructClose(StringBuilder w);
    public abstract int MaxIdentifierLength { get; }
    public abstract void ValidateFieldName(string name);
    public abstract IReadOnlySet<string> ReservedKeywords { get; }
    public abstract RegexResult ConvertRegex(string re2Pattern);
    public abstract bool SupportsRegex { get; }
    public abstract bool SupportsNativeArrays { get; }
    public abstract bool SupportsJsonb { get; }
    public abstract bool SupportsIndexAnalysis { get; }

    /// <summary>
    /// Default comprehension source: <c>UNNEST(...) AS iterVar</c>.
    /// Dialects that need different aliasing override this.
    /// </summary>
    public virtual void WriteComprehensionSource(StringBuilder w, SqlWriter writeSource, string iterVar)
    {
        WriteUnnest(w, writeSource);
        w.Append(" AS ").Append(iterVar);
    }
}
