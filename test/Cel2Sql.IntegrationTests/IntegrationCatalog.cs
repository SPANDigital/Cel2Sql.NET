using Cel2Sql.Dialects;

namespace Cel2Sql.IntegrationTests;

/// <summary>
/// The shared catalog of integration test cases plus per-dialect capability filtering.
/// Ports the Java <c>AbstractDialectIntegrationTest.testCatalog()</c> /
/// <c>parameterizedTestCatalog()</c> and <c>applyAssumptions()</c>.
/// </summary>
public static class IntegrationCatalog
{
    /// <summary>The main WHERE/expression-only catalog (mirrors Java <c>testCatalog()</c>).</summary>
    public static IReadOnlyList<IntegrationTestCase> TestCatalog { get; } = new List<IntegrationTestCase>
    {
        // --- Basic WHERE clauses ---
        IntegrationTestCase.Where("basic_equality_string", "name == \"Alice\"", "basic", 1),
        IntegrationTestCase.Where("basic_inequality_int", "age != 20", "basic", 1, 2, 3, 4, 6),
        IntegrationTestCase.Where("basic_less_than", "age < 20", "basic", 2, 6),
        IntegrationTestCase.Where("basic_less_equal", "age <= 20", "basic", 2, 5, 6),
        IntegrationTestCase.Where("basic_greater_equal_float", "height >= 1.6180339887", "basic", 1, 2, 3, 4, 5),
        IntegrationTestCase.Where("basic_is_null", "null_var == null", "basic", 2, 4, 6),
        IntegrationTestCase.Where("basic_is_not_null", "null_var != null", "basic", 1, 3, 5),
        IntegrationTestCase.Where("basic_is_true", "adult == true", "basic", 1, 3, 4, 5),
        IntegrationTestCase.Where("basic_not", "!adult", "basic", 2, 6),

        // --- Expression-only ---
        IntegrationTestCase.Expr("expr_string_literal", "\"hello\"", "basic"),
        IntegrationTestCase.Expr("expr_int_literal", "42", "basic"),
        IntegrationTestCase.Expr("expr_double_literal", "3.14", "basic"),
        IntegrationTestCase.Expr("expr_bool_true", "true", "basic"),
        IntegrationTestCase.Expr("expr_negative_int", "-1", "basic"),

        // --- Operators ---
        IntegrationTestCase.Where("op_and", "name == \"Alice\" && age > 20", "operator", 1),
        IntegrationTestCase.Where("op_or", "name == \"Alice\" || age > 20", "operator", 1, 3, 4),
        IntegrationTestCase.Where("op_add", "age + 10 == 40", "operator", 1),
        IntegrationTestCase.Where("op_modulo", "age % 10 == 0", "operator", 1, 5, 6),

        // --- Strings ---
        IntegrationTestCase.Where("string_startsWith", "name.startsWith(\"Al\")", "string", 1),
        IntegrationTestCase.Where("string_endsWith", "name.endsWith(\"e\")", "string", 1, 4, 5),
        IntegrationTestCase.Where("string_contains", "name.contains(\"ar\")", "string", 3, 6),
        IntegrationTestCase.Where("string_size", "size(name) == 3", "string", 2, 5),

        // --- Regex (skip SQLite) ---
        IntegrationTestCase.Where("regex_matches", "name.matches(\"^Ali.*\")", "regex", 1),

        // --- Arrays: IN-list (all dialects) ---
        IntegrationTestCase.Where("array_in_list", "name in [\"Alice\", \"Bob\", \"Carol\"]", "array_in", 1, 2, 3),

        // --- Arrays: native (skip MySQL/SQLite) ---
        IntegrationTestCase.Expr("array_index_literal", "[1, 2, 3][0] == 1", "array_native"),

        // --- Comprehensions (skip MySQL, skip SQLite) ---
        IntegrationTestCase.Where("comp_all", "string_list.all(x, x != \"bad\")", "comprehension", 2, 4, 6),
        IntegrationTestCase.Where("comp_exists", "string_list.exists(x, x == \"good\")", "comprehension", 1, 2, 4, 5),
        IntegrationTestCase.Where("comp_exists_one", "string_list.exists_one(x, x == \"unique\")", "comprehension", 4, 5),

        // --- Timestamps ---
        IntegrationTestCase.Where("timestamp_getFullYear", "created_at.getFullYear() == 2024", "timestamp", 1, 2, 3, 5, 6),
        IntegrationTestCase.Where("timestamp_getHours", "created_at.getHours() == 10", "timestamp", 1),
        IntegrationTestCase.Where("timestamp_getDayOfMonth", "created_at.getDayOfMonth() == 25", "timestamp", 4),

        // --- Casts (expression-only) ---
        IntegrationTestCase.Expr("cast_int_from_string", "int(\"42\") == 42", "cast"),
        IntegrationTestCase.Expr("cast_string_from_int", "string(42) == \"42\"", "cast"),
    };

    /// <summary>The parameterized catalog (mirrors Java <c>parameterizedTestCatalog()</c>).</summary>
    public static IReadOnlyList<IntegrationTestCase> ParameterizedTestCatalog { get; } = new List<IntegrationTestCase>
    {
        IntegrationTestCase.Where("param_string_eq", "name == \"Alice\"", "parameterized", 1),
        IntegrationTestCase.Where("param_int_gt", "age > 30", "parameterized", 4),
        IntegrationTestCase.Where("param_compound", "name == \"Alice\" && age > 20", "parameterized", 1),
        IntegrationTestCase.Where("param_bool_inlined", "active == true", "parameterized", 1, 3, 5),
    };

    /// <summary>
    /// Returns true when the case should run for the given dialect. Replaces the JUnit
    /// <c>Assumptions</c> in the Java <c>applyAssumptions()</c>: filtering at member-data
    /// build time, since xUnit v2 has no dynamic skip.
    /// </summary>
    public static bool AppliesTo(IntegrationTestCase tc, IDialect dialect)
    {
        return tc.Category switch
        {
            "regex" => dialect.SupportsRegex,
            "array_native" => dialect.SupportsNativeArrays,
            "comprehension" => dialect.Name != DialectName.MySql,
            _ => true,
        };
    }

    /// <summary>
    /// Serializable member-data rows for the WHERE/expression-only catalog, filtered for
    /// the dialect's capabilities. Each row is
    /// <c>[Name, Cel, int[] ExpectedRowIds, bool ExpressionOnly, Category]</c>.
    /// </summary>
    public static IEnumerable<object[]> SqlCasesFor(IDialect dialect) => CasesFor(TestCatalog, dialect);

    /// <summary>Serializable member-data rows for the parameterized catalog.</summary>
    public static IEnumerable<object[]> ParamCasesFor(IDialect dialect) => CasesFor(ParameterizedTestCatalog, dialect);

    private static IEnumerable<object[]> CasesFor(IEnumerable<IntegrationTestCase> catalog, IDialect dialect)
    {
        foreach (var tc in catalog)
        {
            if (!AppliesTo(tc, dialect)) continue;
            yield return new object[] { tc.Name, tc.Cel, tc.ExpectedRowIds, tc.ExpressionOnly, tc.Category };
        }
    }
}
