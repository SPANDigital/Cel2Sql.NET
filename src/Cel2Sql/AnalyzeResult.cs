using Cel2Sql.Dialects;

namespace Cel2Sql;

/// <summary>
/// Result of query analysis containing both the SQL output and index recommendations.
/// </summary>
/// <param name="Sql">The converted SQL WHERE clause.</param>
/// <param name="Recommendations">List of index recommendations for query optimization.</param>
public sealed record AnalyzeResult(string Sql, IReadOnlyList<IndexRecommendation> Recommendations);
