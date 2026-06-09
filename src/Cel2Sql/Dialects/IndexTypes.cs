namespace Cel2Sql.Dialects;

/// <summary>Result of converting an RE2 regex pattern to a dialect-specific format.</summary>
/// <param name="Pattern">The converted pattern in the dialect's native format.</param>
/// <param name="CaseInsensitive">Whether the match should be case-insensitive.</param>
public sealed record RegexResult(string Pattern, bool CaseInsensitive);

/// <summary>
/// Enumerates detected index-worthy query patterns.
/// Used by the index advisor to generate dialect-specific index recommendations.
/// </summary>
public enum PatternType
{
    /// <summary>Equality/range comparisons (==, &gt;, &lt;, &gt;=, &lt;=).</summary>
    Comparison,
    /// <summary>JSON/JSONB field access.</summary>
    JsonAccess,
    /// <summary>Regex pattern matching.</summary>
    RegexMatch,
    /// <summary>Array IN/containment.</summary>
    ArrayMembership,
    /// <summary>Array comprehension (all, exists, filter, map).</summary>
    ArrayComprehension,
    /// <summary>JSON array comprehension.</summary>
    JsonArrayComprehension,
}

/// <summary>Describes a detected query pattern that could benefit from indexing.</summary>
/// <param name="Column">The full column name (e.g., "person.metadata").</param>
/// <param name="Pattern">The type of query pattern detected.</param>
/// <param name="TableHint">
/// Optional table name hint for generating CREATE INDEX statements;
/// if null or empty, "table_name" is used as the default placeholder.
/// </param>
public sealed record IndexPattern(string Column, PatternType Pattern, string? TableHint = null);

/// <summary>
/// Represents a database index recommendation.
/// Provides actionable guidance for optimizing query performance.
/// </summary>
/// <param name="Column">The database column that should be indexed.</param>
/// <param name="IndexType">The index type (e.g., "BTREE", "GIN", "ART", "CLUSTERING").</param>
/// <param name="Expression">The complete DDL statement that can be executed directly.</param>
/// <param name="Reason">Explains why this index is recommended.</param>
public sealed record IndexRecommendation(string Column, string IndexType, string Expression, string Reason);

/// <summary>
/// Generates dialect-specific index recommendations.
/// Dialects that support index analysis implement this interface.
/// </summary>
public interface IIndexAdvisor
{
    /// <summary>
    /// Generates an <see cref="IndexRecommendation"/> for the given pattern,
    /// or returns null if the dialect has no applicable index for this pattern.
    /// </summary>
    IndexRecommendation? RecommendIndex(IndexPattern pattern);

    /// <summary>Returns which <see cref="PatternType"/>s this advisor can handle.</summary>
    IReadOnlyList<PatternType> SupportedPatterns();
}
