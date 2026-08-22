using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Cel2Sql.Cel;
using Cel2Sql.Dialects;
using Cel2Sql.Errors;
using Cel2Sql.Schema;
using Microsoft.Extensions.Logging;

namespace Cel2Sql;

/// <summary>
/// Core AST visitor that converts a checked CEL expression into a SQL WHERE clause.
/// This is the main workhorse of the cel2sql conversion process.
///
/// <para>Ported from Go's <c>cel2sql.converter</c> struct in <c>cel2sql.go</c> via the
/// Java cel2sql4j port.</para>
/// </summary>
internal sealed class Converter
{
    // ========================================================================
    // CEL Operator Constants (from Go's cel-go operators package)
    // ========================================================================

    internal const string CONDITIONAL = "_?_:_";
    internal const string LOGICAL_AND = "_&&_";
    internal const string LOGICAL_OR = "_||_";
    internal const string LOGICAL_NOT = "!_";
    internal const string NEGATE = "-_";
    internal const string EQUALS = "_==_";
    internal const string NOT_EQUALS = "_!=_";
    internal const string LESS = "_<_";
    internal const string LESS_EQUALS = "_<=_";
    internal const string GREATER = "_>_";
    internal const string GREATER_EQUALS = "_>=_";
    internal const string ADD = "_+_";
    internal const string SUBTRACT = "_-_";
    internal const string MULTIPLY = "_*_";
    internal const string DIVIDE = "_/_";
    internal const string MODULO = "_%_";
    internal const string INDEX = "_[_]";
    internal const string IN = "@in";
    internal const string OLD_IN = "_in_";
    internal const string NOT_STRICTLY_FALSE = "@not_strictly_false";

    // ========================================================================
    // CEL Overload Constants (from Go's cel-go overloads package)
    // ========================================================================

    internal const string CONTAINS = "contains";
    internal const string STARTS_WITH = "startsWith";
    internal const string ENDS_WITH = "endsWith";
    internal const string MATCHES = "matches";
    internal const string SIZE = "size";
    internal const string TYPE_CONVERT_BOOL = "bool";
    internal const string TYPE_CONVERT_BYTES = "bytes";
    internal const string TYPE_CONVERT_DOUBLE = "double";
    internal const string TYPE_CONVERT_INT = "int";
    internal const string TYPE_CONVERT_STRING = "string";
    internal const string TYPE_CONVERT_UINT = "uint";
    internal const string TYPE_CONVERT_DURATION = "duration";
    internal const string TYPE_CONVERT_TIMESTAMP = "timestamp";
    internal const string TIME_GET_FULL_YEAR = "getFullYear";
    internal const string TIME_GET_MONTH = "getMonth";
    internal const string TIME_GET_DATE = "getDate";
    internal const string TIME_GET_HOURS = "getHours";
    internal const string TIME_GET_MINUTES = "getMinutes";
    internal const string TIME_GET_SECONDS = "getSeconds";
    internal const string TIME_GET_MILLISECONDS = "getMilliseconds";
    internal const string TIME_GET_DAY_OF_YEAR = "getDayOfYear";
    internal const string TIME_GET_DAY_OF_MONTH = "getDayOfMonth";
    internal const string TIME_GET_DAY_OF_WEEK = "getDayOfWeek";

    // Additional string function overloads
    internal const string LOWER_ASCII = "lowerAscii";
    internal const string UPPER_ASCII = "upperAscii";
    internal const string TRIM = "trim";
    internal const string CHAR_AT = "charAt";
    internal const string INDEX_OF = "indexOf";
    internal const string LAST_INDEX_OF = "lastIndexOf";
    internal const string SUBSTRING = "substring";
    internal const string REPLACE = "replace";
    internal const string REVERSE = "reverse";
    internal const string SPLIT = "split";
    internal const string JOIN = "join";
    internal const string FORMAT = "format";

    // Format strings are bounded to keep generated SQL small. Mirrors upstream cel2sql.
    internal const int MAX_FORMAT_STRING_LENGTH = 1000;

    // ========================================================================
    // Operator Precedence Map
    // ========================================================================

    private static readonly IReadOnlyDictionary<string, int> PRECEDENCE_MAP = new Dictionary<string, int>
    {
        { CONDITIONAL, 8 },
        { LOGICAL_OR, 7 },
        { LOGICAL_AND, 6 },
        { EQUALS, 5 },
        { NOT_EQUALS, 5 },
        { LESS, 5 },
        { LESS_EQUALS, 5 },
        { GREATER, 5 },
        { GREATER_EQUALS, 5 },
        { IN, 5 },
        { OLD_IN, 5 },
        { ADD, 4 },
        { SUBTRACT, 4 },
        { MULTIPLY, 3 },
        { DIVIDE, 3 },
        { MODULO, 3 },
        { NEGATE, 2 },
        { INDEX, 1 },
    };

    // ========================================================================
    // Reverse Binary Operator Map (CEL operator -> SQL symbol)
    // ========================================================================

    private static readonly IReadOnlyDictionary<string, string> REVERSE_BINARY_OP_MAP = new Dictionary<string, string>
    {
        { ADD, "+" },
        { SUBTRACT, "-" },
        { MULTIPLY, "*" },
        { DIVIDE, "/" },
        { MODULO, "%" },
        { EQUALS, "=" },
        { NOT_EQUALS, "!=" },
        { LESS, "<" },
        { LESS_EQUALS, "<=" },
        { GREATER, ">" },
        { GREATER_EQUALS, ">=" },
    };

    // ========================================================================
    // Duration unit pattern for parsing Go-style duration strings
    // ========================================================================

    private static readonly Regex DURATION_UNIT_PATTERN = new(@"(\d+)(h|m(?!s)|s|ms|us|ns)");

    // ========================================================================
    // Maximum comprehension nesting depth
    // ========================================================================

    private const int MAX_COMPREHENSION_DEPTH = 5;

    // ========================================================================
    // Instance Fields
    // ========================================================================

    private readonly CelAst _ast;
    private readonly StringBuilder _str = new();
    private readonly IReadOnlyDictionary<string, Schema.Schema>? _schemas;
    private readonly ILogger _logger;
    private readonly IDialect _dialect;
    private readonly int _maxDepth;
    private readonly int _maxOutputLength;
    private readonly bool _parameterize;
    private readonly IReadOnlySet<string> _jsonVariables;
    private readonly IReadOnlyDictionary<string, string> _columnAliases;
    private readonly List<object?> _parameters = new();
    private int _depth = 0;
    private int _comprehensionDepth = 0;
    private int _paramCount;

    // Maximum length of a byte literal that may be inlined into SQL. Each
    // byte expands to roughly 4 characters (e.g. \xDE), so 10 000 bytes ≈ 40 KB
    // of generated SQL. Mirrors upstream cel2sql's maxByteArrayLength constant
    // (CWE-400 — uncontrolled resource consumption). The check is bypassed in
    // parameterized mode since bytes are sent directly to the JDBC driver.
    internal const int MAX_BYTE_ARRAY_LENGTH = 10_000;

    // ========================================================================
    // Constructor
    // ========================================================================

    internal Converter(CelAst ast, ConvertOptions opts, bool parameterize)
    {
        _ast = ast;
        _schemas = opts.Schemas;
        _logger = opts.Logger;
        _dialect = opts.Dialect!;
        _maxDepth = opts.MaxDepth;
        _maxOutputLength = opts.MaxOutputLength;
        _parameterize = parameterize;
        _jsonVariables = opts.JsonVariables;
        _columnAliases = opts.ColumnAliases;
        // paramCount is incremented before use, so start one below the configured index.
        _paramCount = opts.ParamStartIndex - 1;
    }

    // ========================================================================
    // Public API
    // ========================================================================

    /// <summary>Converts the AST to a SQL string.</summary>
    internal string Convert()
    {
        Visit(_ast.Expr);
        return _str.ToString();
    }

    /// <summary>Returns the collected parameter values (for parameterized mode).</summary>
    internal IReadOnlyList<object?> GetParameters()
    {
        return _parameters.ToArray();
    }

    /// <summary>
    /// Walks the AST to collect index recommendations without generating SQL.
    /// This is used by the analysis engine.
    /// </summary>
    internal IReadOnlyList<IndexRecommendation> CollectIndexRecommendations(IIndexAdvisor advisor)
    {
        var recommendations = new Dictionary<string, IndexRecommendation>();
        AnalyzeExpr(_ast.Expr, advisor, recommendations);
        return recommendations.Values.ToArray();
    }

    private void AnalyzeExpr(CelExprNode? expr, IIndexAdvisor advisor, Dictionary<string, IndexRecommendation> recommendations)
    {
        if (expr == null) return;
        CelExprKind kind = expr.Kind;
        switch (kind)
        {
            case CelExprKind.Call:
            {
                CelCallNode call = expr.Call();
                string fn = call.Function;
                // Analyze comparison operators for index recommendations
                if (fn == EQUALS || fn == NOT_EQUALS || fn == LESS || fn == LESS_EQUALS
                        || fn == GREATER || fn == GREATER_EQUALS)
                {
                    if (call.Args.Count >= 2)
                    {
                        string? col = ExtractColumnName(call.Args[0]);
                        if (col != null)
                        {
                            AddRecommendation(recommendations, advisor, col, PatternType.Comparison);
                        }
                    }
                }
                // Analyze regex matches
                if (fn == MATCHES)
                {
                    CelExprNode? target = call.Target != null ? call.Target : (call.Args.Count >= 1 ? call.Args[0] : null);
                    if (target != null)
                    {
                        string? col = ExtractColumnName(target);
                        if (col != null)
                        {
                            AddRecommendation(recommendations, advisor, col, PatternType.RegexMatch);
                        }
                    }
                }
                // Analyze IN operators
                if (fn == IN || fn == OLD_IN)
                {
                    if (call.Args.Count >= 2)
                    {
                        string? col = ExtractColumnName(call.Args[1]);
                        if (col != null)
                        {
                            AddRecommendation(recommendations, advisor, col, PatternType.ArrayMembership);
                        }
                    }
                }
                // Recurse into target and arguments
                if (call.Target != null) AnalyzeExpr(call.Target, advisor, recommendations);
                foreach (CelExprNode arg in call.Args)
                {
                    AnalyzeExpr(arg, advisor, recommendations);
                }
                break;
            }
            case CelExprKind.Comprehension:
            {
                CelComprehensionNode comp = expr.Comprehension();
                string? col = ExtractColumnName(comp.IterRange);
                if (col != null)
                {
                    AddRecommendation(recommendations, advisor, col, PatternType.ArrayComprehension);
                }
                AnalyzeExpr(comp.IterRange, advisor, recommendations);
                AnalyzeExpr(comp.AccuInit, advisor, recommendations);
                AnalyzeExpr(comp.LoopCondition, advisor, recommendations);
                AnalyzeExpr(comp.LoopStep, advisor, recommendations);
                AnalyzeExpr(comp.Result, advisor, recommendations);
                break;
            }
            case CelExprKind.Select:
            {
                CelSelectNode sel = expr.Select();
                AnalyzeExpr(sel.Operand, advisor, recommendations);
                break;
            }
            case CelExprKind.List:
            {
                CelListNode list = expr.List();
                foreach (CelExprNode elem in list.Elements)
                {
                    AnalyzeExpr(elem, advisor, recommendations);
                }
                break;
            }
            default:
                /* CONSTANT, IDENT, etc. - nothing to analyze */
                break;
        }
    }

    private string? ExtractColumnName(CelExprNode? expr)
    {
        if (expr == null) return null;
        if (expr.Kind == CelExprKind.Ident)
        {
            return expr.Ident().Name;
        }
        if (expr.Kind == CelExprKind.Select)
        {
            CelSelectNode sel = expr.Select();
            string? operandName = ExtractColumnName(sel.Operand);
            if (operandName != null)
            {
                return operandName + "." + sel.Field;
            }
            return sel.Field;
        }
        return null;
    }

    private void AddRecommendation(Dictionary<string, IndexRecommendation> recommendations, IIndexAdvisor advisor, string column, PatternType pattern)
    {
        IndexRecommendation? rec = advisor.RecommendIndex(new IndexPattern(column, pattern));
        if (rec == null) return;
        if (!recommendations.TryGetValue(column, out var existing))
        {
            recommendations[column] = rec;
        }
        else
        {
            // More specialized index types take priority over basic ones
            if (IsBasicIndexType(existing.IndexType) && !IsBasicIndexType(rec.IndexType))
            {
                recommendations[column] = rec;
            }
        }
    }

    private static bool IsBasicIndexType(string indexType)
    {
        return indexType == "BTREE" || indexType == "ART" || indexType == "CLUSTERING";
    }

    // ========================================================================
    // Core Visitor Dispatch
    // ========================================================================

    /// <summary>
    /// Main visitor dispatch. Routes to the appropriate VisitXxx method based on expression kind.
    /// </summary>
    private void Visit(CelExprNode expr)
    {
        _depth++;
        try
        {
            if (_depth > _maxDepth)
            {
                throw ConversionException.Of(
                        ErrorMessages.ConversionFailed,
                        "maximum recursion depth " + _maxDepth + " exceeded");
            }
            if (_str.Length > _maxOutputLength)
            {
                throw ConversionException.Of(
                        ErrorMessages.ConversionFailed,
                        "output length exceeds maximum of " + _maxOutputLength + " characters");
            }
            CelExprKind kind = expr.Kind;
            switch (kind)
            {
                case CelExprKind.Call: VisitCall(expr); break;
                case CelExprKind.Comprehension: VisitComprehension(expr); break;
                case CelExprKind.Constant: VisitConst(expr); break;
                case CelExprKind.Ident: VisitIdent(expr); break;
                case CelExprKind.List: VisitList(expr); break;
                case CelExprKind.Select: VisitSelect(expr); break;
                case CelExprKind.Struct: VisitStruct(expr); break;
                case CelExprKind.Map: VisitStructMap(expr); break;
                default:
                    throw ConversionException.Of(
                        ErrorMessages.UnsupportedExpression,
                        "unsupported expression kind: " + kind);
            }
        }
        finally
        {
            _depth--;
        }
    }

    // ========================================================================
    // Type Helpers
    // ========================================================================

    /// <summary>Gets the type of an expression from the AST type map.</summary>
    private CelTypeRef? GetType(CelExprNode expr)
    {
        return _ast.GetType(expr.Id);
    }

    /// <summary>Checks if a type represents a string.</summary>
    private static bool IsStringType(CelTypeRef? type)
    {
        return type != null && type.Kind == CelTypeKind.String;
    }

    /// <summary>Checks if a type represents a list/array.</summary>
    private static bool IsListType(CelTypeRef? type)
    {
        return type != null && type.Kind == CelTypeKind.List;
    }

    /// <summary>Checks if a type represents a map.</summary>
    private static bool IsMapType(CelTypeRef? type)
    {
        return type != null && type.Kind == CelTypeKind.Map;
    }

    /// <summary>Checks if a type represents a timestamp.</summary>
    private static bool IsTimestampType(CelTypeRef? type)
    {
        return type != null && type.Kind == CelTypeKind.Timestamp;
    }

    /// <summary>Checks if a type represents a duration.</summary>
    private static bool IsDurationType(CelTypeRef? type)
    {
        return type != null && type.Kind == CelTypeKind.Duration;
    }

    /// <summary>Checks if a type is numeric (int, uint, double).</summary>
    private static bool IsNumericType(CelTypeRef? type)
    {
        if (type == null) return false;
        return type.Kind == CelTypeKind.Int || type.Kind == CelTypeKind.Uint || type.Kind == CelTypeKind.Double;
    }

    /// <summary>Checks if a comparison involves numeric types on both sides.</summary>
    private bool IsNumericComparison(CelExprNode lhs, CelExprNode rhs)
    {
        CelTypeRef? lhsType = GetType(lhs);
        CelTypeRef? rhsType = GetType(rhs);
        return IsNumericType(lhsType) && IsNumericType(rhsType);
    }

    // ========================================================================
    // Literal Detection Helpers
    // ========================================================================

    /// <summary>Checks if an expression is a null literal.</summary>
    private static bool IsNullLiteral(CelExprNode expr)
    {
        return expr.Kind == CelExprKind.Constant
                && expr.Constant().Kind == CelConstantKind.NullValue;
    }

    /// <summary>Checks if an expression is a boolean literal (true or false).</summary>
    private static bool IsBoolLiteral(CelExprNode expr)
    {
        return expr.Kind == CelExprKind.Constant
                && expr.Constant().Kind == CelConstantKind.BooleanValue;
    }

    /// <summary>Checks if an expression is a string literal.</summary>
    private static bool IsStringLiteral(CelExprNode expr)
    {
        return expr.Kind == CelExprKind.Constant
                && expr.Constant().Kind == CelConstantKind.StringValue;
    }

    /// <summary>Checks if an expression is an int64 literal with value 0.</summary>
    private static bool IsIntZero(CelExprNode expr)
    {
        return expr.Kind == CelExprKind.Constant
                && expr.Constant().Kind == CelConstantKind.Int64Value
                && expr.Constant().Int64Value == 0;
    }

    /// <summary>Checks if an expression is a boolean literal true.</summary>
    private static bool IsBoolTrue(CelExprNode expr)
    {
        return IsBoolLiteral(expr) && expr.Constant().BooleanValue;
    }

    /// <summary>Checks if an expression is a boolean literal false.</summary>
    private static bool IsBoolFalse(CelExprNode expr)
    {
        return IsBoolLiteral(expr) && !expr.Constant().BooleanValue;
    }

    /// <summary>Checks if an expression is an empty list literal.</summary>
    private static bool IsEmptyList(CelExprNode expr)
    {
        return expr.Kind == CelExprKind.List && expr.List().Elements.Count == 0;
    }

    /// <summary>Checks if an expression is a field access (identifier or select).</summary>
    private static bool IsFieldAccessExpression(CelExprNode expr)
    {
        return expr.Kind == CelExprKind.Ident || expr.Kind == CelExprKind.Select;
    }

    // ========================================================================
    // VisitConst - Constant Literals
    // ========================================================================

    /// <summary>Visits a constant expression and writes the SQL literal.</summary>
    private void VisitConst(CelExprNode expr)
    {
        CelConstantNode c = expr.Constant();
        switch (c.Kind)
        {
            case CelConstantKind.BooleanValue:
                _str.Append(c.BooleanValue ? "TRUE" : "FALSE");
                break;
            case CelConstantKind.NullValue:
                _str.Append("NULL");
                break;
            case CelConstantKind.Int64Value:
            {
                if (_parameterize)
                {
                    WriteParam(c.Int64Value);
                }
                else
                {
                    _str.Append(c.Int64Value);
                }
                break;
            }
            case CelConstantKind.Uint64Value:
            {
                long val = (long)c.Uint64Value;
                if (_parameterize)
                {
                    WriteParam(val);
                }
                else
                {
                    _str.Append(val);
                }
                break;
            }
            case CelConstantKind.DoubleValue:
            {
                if (_parameterize)
                {
                    WriteParam(c.DoubleValue);
                }
                else
                {
                    _str.Append(c.DoubleValue.ToString(CultureInfo.InvariantCulture));
                }
                break;
            }
            case CelConstantKind.StringValue:
            {
                if (_parameterize)
                {
                    WriteParam(c.StringValue);
                }
                else
                {
                    _dialect.WriteStringLiteral(_str, c.StringValue);
                }
                break;
            }
            case CelConstantKind.BytesValue:
            {
                byte[] bytes = c.BytesValue;
                if (_parameterize)
                {
                    WriteParam(bytes);
                }
                else
                {
                    if (bytes.Length > MAX_BYTE_ARRAY_LENGTH)
                    {
                        throw ConversionException.Of(
                                ErrorMessages.ConversionFailed,
                                "byte literal length " + bytes.Length
                                        + " exceeds maximum of " + MAX_BYTE_ARRAY_LENGTH);
                    }
                    _dialect.WriteBytesLiteral(_str, bytes);
                }
                break;
            }
            default:
                throw ConversionException.Of(
                    ErrorMessages.UnsupportedType,
                    "unsupported constant kind: " + c.Kind);
        }
    }

    /// <summary>Writes a parameter placeholder and records the value.</summary>
    private void WriteParam(object? value)
    {
        _paramCount++;
        _parameters.Add(value);
        _dialect.WriteParamPlaceholder(_str, _paramCount);
    }

    // ========================================================================
    // VisitIdent - Identifiers
    // ========================================================================

    /// <summary>
    /// Visits an identifier expression, validates it, and writes the SQL identifier.
    /// If the identifier matches a key in the column-alias map, the alias is
    /// emitted instead (validated against the dialect's identifier rules).
    /// </summary>
    private void VisitIdent(CelExprNode expr)
    {
        string name = expr.Ident().Name;
        if (_columnAliases.TryGetValue(name, out var alias) && alias != null)
        {
            _dialect.ValidateFieldName(alias);
            _str.Append(alias);
            return;
        }
        _dialect.ValidateFieldName(name);
        _str.Append(name);
    }

    // ========================================================================
    // VisitSelect - Field Selection (including JSON paths)
    // ========================================================================

    /// <summary>
    /// Visits a select (field access) expression. Handles:
    /// - Regular field access: table.field
    /// - has() macro: field presence test
    /// - JSON path access: json_col->'field'->>'nested'
    /// </summary>
    private void VisitSelect(CelExprNode expr)
    {
        CelSelectNode sel = expr.Select();

        // Handle has() macro
        if (sel.TestOnly)
        {
            VisitHasField(expr);
            return;
        }

        // Check for JSON path access
        if (ShouldUseJSONPath(expr))
        {
            VisitJSONSelect(expr);
            return;
        }

        // Regular field access: operand.field
        CelExprNode operand = sel.Operand;
        string field = sel.Field;
        _dialect.ValidateFieldName(field);

        // If the operand is an ident, this is table.field or simple field access
        if (operand.Kind == CelExprKind.Ident)
        {
            Visit(operand);
            _str.Append('.');
            _str.Append(field);
        }
        else
        {
            Visit(operand);
            _str.Append('.');
            _str.Append(field);
        }
    }

    /// <summary>
    /// Handles the has() macro which tests for field presence.
    /// For JSON fields, generates: json_col ? 'field' (JSONB) or json_col->'field' IS NOT NULL (JSON)
    /// For regular fields, generates: field IS NOT NULL
    /// </summary>
    private void VisitHasField(CelExprNode expr)
    {
        CelSelectNode sel = expr.Select();
        CelExprNode operand = sel.Operand;
        string field = sel.Field;

        // Check if this is a JSON field
        TableAndField? tf = GetTableAndFieldFromSelectChain(operand);
        if (tf != null)
        {
            FieldSchema? fieldSchema = FindFieldSchema(tf.Table, tf.Field);
            if (fieldSchema != null && fieldSchema.IsJson)
            {
                bool isJSONB = fieldSchema.IsJsonb;
                // Check for nested JSON path
                if (HasJSONFieldInChain(operand) && operand.Kind == CelExprKind.Select)
                {
                    // Nested JSON: build a path and use jsonb_extract_path_text IS NOT NULL
                    var path = new List<string> { field };
                    CelExprNode root = BuildJSONPathInternal(operand, path);
                    _dialect.WriteJsonExtractPath(_str, path, () => Visit(root));
                    return;
                }
                _dialect.WriteJsonExistence(_str, isJSONB, field, () => Visit(operand));
                return;
            }
        }

        // Regular has(): field IS NOT NULL
        Visit(operand);
        _str.Append('.');
        _str.Append(field);
        _str.Append(" IS NOT NULL");
    }

    /// <summary>
    /// Visits a JSON field access, building the appropriate -> or ->> operator chain.
    /// </summary>
    private void VisitJSONSelect(CelExprNode expr)
    {
        CelSelectNode sel = expr.Select();
        bool isFinal = IsJSONTextExtraction(expr);

        if (IsNestedJSONAccess(sel.Operand))
        {
            // Intermediate JSON access: use ->
            _dialect.WriteJsonFieldAccess(_str,
                    () => VisitJSONSelect(sel.Operand),
                    sel.Field,
                    isFinal);
        }
        else
        {
            // First level JSON access
            _dialect.WriteJsonFieldAccess(_str,
                    () => Visit(sel.Operand),
                    sel.Field,
                    isFinal);
        }
    }

    // ========================================================================
    // VisitCall - Function Calls and Operators
    // ========================================================================

    /// <summary>Dispatches call expressions based on the function name.</summary>
    private void VisitCall(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        string fun = call.Function;

        switch (fun)
        {
            case CONDITIONAL: VisitCallConditional(expr); break;
            case LOGICAL_AND:
            case LOGICAL_OR: VisitCallBinary(expr); break;
            case EQUALS:
            case NOT_EQUALS: VisitCallEqualityOp(expr); break;
            case LESS:
            case LESS_EQUALS:
            case GREATER:
            case GREATER_EQUALS: VisitCallComparisonOp(expr); break;
            case ADD: VisitCallAdd(expr); break;
            case SUBTRACT: VisitCallSubtract(expr); break;
            case MULTIPLY:
            case DIVIDE:
            case MODULO: VisitCallBinary(expr); break;
            case LOGICAL_NOT: VisitCallUnary(expr); break;
            case NEGATE: VisitCallNegate(expr); break;
            case INDEX: VisitCallIndex(expr); break;
            case IN:
            case OLD_IN: VisitCallIn(expr); break;
            case NOT_STRICTLY_FALSE:
            {
                // Unwrap @not_strictly_false - just visit the inner expression
                if (call.Args.Count != 0)
                {
                    Visit(call.Args[0]);
                }
                break;
            }
            default: VisitCallFunc(expr); break;
        }
    }

    // ========================================================================
    // VisitCallBinary - Binary Operators
    // ========================================================================

    /// <summary>
    /// Visits a binary operator call (AND, OR, arithmetic, comparison).
    /// Handles parenthesization based on operator precedence.
    /// </summary>
    private void VisitCallBinary(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        string fun = call.Function;
        CelExprNode lhs = call.Args[0];
        CelExprNode rhs = call.Args[1];

        string sqlOp;
        switch (fun)
        {
            case LOGICAL_AND: sqlOp = "AND"; break;
            case LOGICAL_OR: sqlOp = "OR"; break;
            default:
            {
                if (!REVERSE_BINARY_OP_MAP.TryGetValue(fun, out var op))
                {
                    throw ConversionException.Of(
                            ErrorMessages.InvalidOperator,
                            "unknown binary operator: " + fun);
                }
                sqlOp = op;
                break;
            }
        }

        VisitMaybeNested(expr, lhs);
        _str.Append(' ').Append(sqlOp).Append(' ');
        VisitMaybeNested(expr, rhs);
    }

    // ========================================================================
    // Equality Operators (==, !=) with IS NULL / IS TRUE / IS FALSE handling
    // ========================================================================

    /// <summary>Handles equality/inequality with special cases for NULL, TRUE, FALSE.</summary>
    private void VisitCallEqualityOp(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        string fun = call.Function;
        CelExprNode lhs = call.Args[0];
        CelExprNode rhs = call.Args[1];
        bool isEquals = EQUALS == fun;

        // Handle NULL comparisons: IS NULL / IS NOT NULL
        if (IsNullLiteral(rhs))
        {
            VisitMaybeNested(expr, lhs);
            _str.Append(isEquals ? " IS NULL" : " IS NOT NULL");
            return;
        }
        if (IsNullLiteral(lhs))
        {
            VisitMaybeNested(expr, rhs);
            _str.Append(isEquals ? " IS NULL" : " IS NOT NULL");
            return;
        }

        // Handle boolean comparisons: IS TRUE / IS NOT TRUE / IS FALSE / IS NOT FALSE
        if (IsBoolLiteral(rhs))
        {
            bool val = rhs.Constant().BooleanValue;
            VisitMaybeNested(expr, lhs);
            if (isEquals)
            {
                _str.Append(val ? " IS TRUE" : " IS FALSE");
            }
            else
            {
                _str.Append(val ? " IS NOT TRUE" : " IS NOT FALSE");
            }
            return;
        }
        if (IsBoolLiteral(lhs))
        {
            bool val = lhs.Constant().BooleanValue;
            VisitMaybeNested(expr, rhs);
            if (isEquals)
            {
                _str.Append(val ? " IS TRUE" : " IS FALSE");
            }
            else
            {
                _str.Append(val ? " IS NOT TRUE" : " IS NOT FALSE");
            }
            return;
        }

        // Handle JSON field comparison with numeric cast
        if (IsJSONFieldRequiringCast(lhs, rhs) || IsJSONFieldRequiringCast(rhs, lhs))
        {
            VisitJSONComparisonWithCast(expr, lhs, rhs, isEquals ? "=" : "!=");
            return;
        }

        // Regular equality
        VisitMaybeNested(expr, lhs);
        _str.Append(isEquals ? " = " : " != ");
        VisitMaybeNested(expr, rhs);
    }

    /// <summary>Handles comparison operators (&lt;, &lt;=, &gt;, &gt;=) with numeric cast for JSON fields.</summary>
    private void VisitCallComparisonOp(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        string fun = call.Function;
        CelExprNode lhs = call.Args[0];
        CelExprNode rhs = call.Args[1];
        string sqlOp = REVERSE_BINARY_OP_MAP[fun];

        // Handle JSON field comparison with numeric cast
        if (IsJSONFieldRequiringCast(lhs, rhs) || IsJSONFieldRequiringCast(rhs, lhs))
        {
            VisitJSONComparisonWithCast(expr, lhs, rhs, sqlOp);
            return;
        }

        VisitMaybeNested(expr, lhs);
        _str.Append(' ').Append(sqlOp).Append(' ');
        VisitMaybeNested(expr, rhs);
    }

    /// <summary>Checks if a JSON field access needs a numeric cast for comparison.</summary>
    private bool IsJSONFieldRequiringCast(CelExprNode field, CelExprNode other)
    {
        if (!ShouldUseJSONPath(field)) return false;
        CelTypeRef? otherType = GetType(other);
        return IsNumericType(otherType);
    }

    /// <summary>Writes a JSON comparison with a ::numeric cast on the JSON extraction side.</summary>
    private void VisitJSONComparisonWithCast(CelExprNode parent, CelExprNode lhs, CelExprNode rhs, string op)
    {
        bool lhsIsJSON = ShouldUseJSONPath(lhs);
        if (lhsIsJSON)
        {
            _str.Append('(');
            VisitMaybeNested(parent, lhs);
            _str.Append(')');
            _dialect.WriteCastToNumeric(_str);
        }
        else
        {
            VisitMaybeNested(parent, lhs);
        }
        _str.Append(' ').Append(op).Append(' ');
        if (!lhsIsJSON && ShouldUseJSONPath(rhs))
        {
            _str.Append('(');
            VisitMaybeNested(parent, rhs);
            _str.Append(')');
            _dialect.WriteCastToNumeric(_str);
        }
        else
        {
            VisitMaybeNested(parent, rhs);
        }
    }

    // ========================================================================
    // Add operator (handles string concat and timestamp arithmetic)
    // ========================================================================

    /// <summary>Handles the + operator. Dispatches to string concat, timestamp arithmetic, or regular add.</summary>
    private void VisitCallAdd(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        CelExprNode lhs = call.Args[0];
        CelExprNode rhs = call.Args[1];

        CelTypeRef? lhsType = GetType(lhs);
        CelTypeRef? rhsType = GetType(rhs);

        // String concatenation
        if (IsStringType(lhsType) && IsStringType(rhsType))
        {
            _dialect.WriteStringConcat(_str,
                    () => VisitMaybeNested(expr, lhs),
                    () => VisitMaybeNested(expr, rhs));
            return;
        }

        // Timestamp + duration
        if (IsTimestampType(lhsType) && IsDurationType(rhsType))
        {
            CallTimestampOperation(lhs, rhs, "+");
            return;
        }

        // Duration + timestamp
        if (IsDurationType(lhsType) && IsTimestampType(rhsType))
        {
            CallTimestampOperation(rhs, lhs, "+");
            return;
        }

        // List concatenation: use || for arrays
        if (IsListType(lhsType) && IsListType(rhsType))
        {
            VisitMaybeNested(expr, lhs);
            _str.Append(" || ");
            VisitMaybeNested(expr, rhs);
            return;
        }

        // Regular arithmetic addition
        VisitCallBinary(expr);
    }

    /// <summary>Handles the - operator. Dispatches to timestamp arithmetic or regular subtract.</summary>
    private void VisitCallSubtract(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        CelExprNode lhs = call.Args[0];
        CelExprNode rhs = call.Args[1];

        CelTypeRef? lhsType = GetType(lhs);
        CelTypeRef? rhsType = GetType(rhs);

        // Timestamp - duration
        if (IsTimestampType(lhsType) && IsDurationType(rhsType))
        {
            CallTimestampOperation(lhs, rhs, "-");
            return;
        }

        // Regular arithmetic subtraction
        VisitCallBinary(expr);
    }

    // ========================================================================
    // Conditional (ternary) operator
    // ========================================================================

    /// <summary>Converts CEL ternary <c>a ? b : c</c> to SQL <c>CASE WHEN a THEN b ELSE c END</c>.</summary>
    private void VisitCallConditional(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        CelExprNode condition = call.Args[0];
        CelExprNode trueExpr = call.Args[1];
        CelExprNode falseExpr = call.Args[2];

        _str.Append("CASE WHEN ");
        Visit(condition);
        _str.Append(" THEN ");
        Visit(trueExpr);
        _str.Append(" ELSE ");
        Visit(falseExpr);
        _str.Append(" END");
    }

    // ========================================================================
    // Unary Operators
    // ========================================================================

    /// <summary>Converts CEL logical not <c>!x</c> to SQL <c>NOT x</c>.</summary>
    private void VisitCallUnary(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        CelExprNode arg = call.Args[0];
        _str.Append("NOT ");
        VisitMaybeNested(expr, arg);
    }

    /// <summary>Converts CEL negation <c>-x</c> to SQL <c>-x</c>.</summary>
    private void VisitCallNegate(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        CelExprNode arg = call.Args[0];
        _str.Append('-');
        VisitMaybeNested(expr, arg);
    }

    // ========================================================================
    // Index Operator (array[i] and map["key"])
    // ========================================================================

    /// <summary>
    /// Handles the index operator: arr[i] or map["key"].
    /// Routes to list index or map index based on the operand type.
    /// </summary>
    private void VisitCallIndex(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        CelExprNode operand = call.Args[0];
        CelExprNode index = call.Args[1];

        CelTypeRef? operandType = GetType(operand);

        if (IsListType(operandType))
        {
            VisitCallListIndex(operand, index);
        }
        else if (IsMapType(operandType))
        {
            VisitCallMapIndex(expr, operand, index);
        }
        else
        {
            // Check for JSON array access
            if (ShouldUseJSONPath(operand))
            {
                // JSON array: operand->index
                Visit(operand);
                _str.Append("->");
                Visit(index);
                return;
            }
            // Default: treat as list index
            VisitCallListIndex(operand, index);
        }
    }

    /// <summary>
    /// Writes a list (array) index expression. Uses dialect-specific 0-to-1 index conversion.
    /// </summary>
    private void VisitCallListIndex(CelExprNode operand, CelExprNode index)
    {
        // Check for JSON array field
        TableAndField? tf = GetTableAndFieldFromSelectChain(operand);
        if (tf != null)
        {
            FieldSchema? fieldSchema = FindFieldSchema(tf.Table, tf.Field);
            if (fieldSchema != null && (fieldSchema.IsJson || fieldSchema.IsJsonb))
            {
                // JSON array index: operand->index
                Visit(operand);
                _str.Append("->");
                Visit(index);
                return;
            }
        }

        // Check if index is a constant int for optimized output
        if (index.Kind == CelExprKind.Constant && index.Constant().Kind == CelConstantKind.Int64Value)
        {
            long idx = index.Constant().Int64Value;
            _dialect.WriteListIndexConst(_str, () => Visit(operand), idx);
        }
        else
        {
            _dialect.WriteListIndex(_str, () => Visit(operand), () => Visit(index));
        }
    }

    /// <summary>Writes a map index expression, converting to JSON field access if needed.</summary>
    private void VisitCallMapIndex(CelExprNode expr, CelExprNode operand, CelExprNode index)
    {
        // For map types, we treat map["key"] as a JSON field access if applicable
        if (IsStringLiteral(index))
        {
            string key = index.Constant().StringValue;
            _dialect.ValidateFieldName(key);
            // If operand is a JSON field, use JSON access syntax
            if (ShouldUseJSONPath(operand))
            {
                bool isFinal = IsJSONTextExtraction(expr);
                _dialect.WriteJsonFieldAccess(_str, () => Visit(operand), key, isFinal);
                return;
            }
        }
        // Default: array-style index
        Visit(operand);
        _str.Append('[');
        Visit(index);
        _str.Append(']');
    }

    // ========================================================================
    // IN Operator
    // ========================================================================

    /// <summary>
    /// Handles the 'in' operator: elem in list.
    /// Supports: regular arrays, JSON arrays, and literal lists.
    /// </summary>
    private void VisitCallIn(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        CelExprNode elem = call.Args[0];
        CelExprNode collection = call.Args[1];

        CelTypeRef? collectionType = GetType(collection);

        // Check if the collection is a JSON array field
        if (ShouldUseJSONPath(collection))
        {
            VisitInJSONArray(elem, collection);
            return;
        }

        // Check if the collection is a regular array field
        if (IsListType(collectionType))
        {
            // Check for JSON array in schema
            TableAndField? tf = GetTableAndFieldFromSelectChain(collection);
            if (tf != null)
            {
                FieldSchema? fieldSchema = FindFieldSchema(tf.Table, tf.Field);
                if (fieldSchema != null && (fieldSchema.IsJson || fieldSchema.IsJsonb))
                {
                    VisitInJSONArray(elem, collection);
                    return;
                }
            }

            // Regular array: elem = ANY(array)
            _dialect.WriteArrayMembership(_str,
                    () => Visit(elem),
                    () => Visit(collection));
            return;
        }

        // Map: check key existence via ? operator or similar
        if (IsMapType(collectionType))
        {
            // For maps, "key in map" becomes: map ? 'key'
            if (ShouldUseJSONPath(collection) || IsJSONObjectFieldAccess(collection))
            {
                _dialect.WriteJsonExistence(_str, true, GetStringValue(elem), () => Visit(collection));
                return;
            }
        }

        // Inline list: elem IN (val1, val2, ...)
        if (collection.Kind == CelExprKind.List)
        {
            CelListNode list = collection.List();
            IReadOnlyList<CelExprNode> elements = list.Elements;
            if (elements.Count == 0)
            {
                _str.Append("FALSE");
                return;
            }
            Visit(elem);
            _str.Append(" IN (");
            for (int i = 0; i < elements.Count; i++)
            {
                if (i > 0) _str.Append(", ");
                Visit(elements[i]);
            }
            _str.Append(')');
            return;
        }

        // Default: use = ANY()
        _dialect.WriteArrayMembership(_str,
                () => Visit(elem),
                () => Visit(collection));
    }

    /// <summary>
    /// Handles IN for JSON arrays: elem = ANY(ARRAY(SELECT jsonb_array_elements_text(...)))
    /// </summary>
    private void VisitInJSONArray(CelExprNode elem, CelExprNode collection)
    {
        TableAndField? tf = GetTableAndFieldFromSelectChain(collection);
        if (tf != null)
        {
            FieldSchema? fieldSchema = FindFieldSchema(tf.Table, tf.Field);
            if (fieldSchema != null)
            {
                bool isJSONB = fieldSchema.IsJsonb;
                string jsonFunc = GetJSONArrayFunction(isJSONB, true);
                _dialect.WriteJsonArrayMembership(_str, jsonFunc, () => Visit(elem), () => Visit(collection));
                return;
            }
        }

        // If no schema found, try nested JSON access
        if (IsNestedJSONAccess(collection))
        {
            _dialect.WriteNestedJsonArrayMembership(_str, () => Visit(elem), () => Visit(collection));
            return;
        }

        // Fallback to standard array membership
        _dialect.WriteArrayMembership(_str,
                () => Visit(elem),
                () => Visit(collection));
    }

    // ========================================================================
    // VisitCallFunc - Named Function Calls
    // ========================================================================

    /// <summary>Dispatches named function calls (contains, startsWith, size, int, string, etc.)</summary>
    private void VisitCallFunc(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        string fun = call.Function;

        switch (fun)
        {
            case CONTAINS: CallContains(expr); break;
            case STARTS_WITH: CallStartsWith(expr); break;
            case ENDS_WITH: CallEndsWith(expr); break;
            case MATCHES: CallMatches(expr); break;
            case SIZE: CallSize(expr); break;
            case LOWER_ASCII: CallLowerAscii(expr); break;
            case UPPER_ASCII: CallUpperAscii(expr); break;
            case TRIM: CallTrim(expr); break;
            case CHAR_AT: CallCharAt(expr); break;
            case INDEX_OF: CallIndexOf(expr); break;
            case LAST_INDEX_OF: CallLastIndexOf(expr); break;
            case SUBSTRING: CallSubstring(expr); break;
            case REPLACE: CallReplace(expr); break;
            case REVERSE: CallReverse(expr); break;
            case SPLIT: CallSplit(expr); break;
            case JOIN: CallJoin(expr); break;
            case FORMAT: CallFormat(expr); break;
            case TYPE_CONVERT_BOOL:
            case TYPE_CONVERT_BYTES:
            case TYPE_CONVERT_DOUBLE:
            case TYPE_CONVERT_INT:
            case TYPE_CONVERT_STRING:
            case TYPE_CONVERT_UINT: CallCasting(expr); break;
            case TYPE_CONVERT_DURATION: CallDuration(expr); break;
            case TYPE_CONVERT_TIMESTAMP: CallTimestampFromString(expr); break;
            case TIME_GET_FULL_YEAR:
            case TIME_GET_MONTH:
            case TIME_GET_DATE:
            case TIME_GET_HOURS:
            case TIME_GET_MINUTES:
            case TIME_GET_SECONDS:
            case TIME_GET_MILLISECONDS:
            case TIME_GET_DAY_OF_YEAR:
            case TIME_GET_DAY_OF_MONTH:
            case TIME_GET_DAY_OF_WEEK: CallExtractFromTimestamp(expr); break;
            default:
                throw ConversionException.Of(
                    ErrorMessages.UnsupportedExpression,
                    "unsupported function: " + fun);
        }
    }

    // ========================================================================
    // String Functions
    // ========================================================================

    /// <summary>Converts str.contains(substr) to POSITION(substr IN str) &gt; 0.</summary>
    private void CallContains(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        CelExprNode target = call.Target
                ?? throw ConversionException.Of(ErrorMessages.InvalidArguments, "contains() requires a target");
        CelExprNode needle = call.Args[0];

        _dialect.WriteContains(_str,
                () => Visit(target),
                () => Visit(needle));
    }

    /// <summary>Converts str.startsWith(prefix) to str LIKE 'prefix%' ESCAPE.</summary>
    private void CallStartsWith(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        CelExprNode target = call.Target
                ?? throw ConversionException.Of(ErrorMessages.InvalidArguments, "startsWith() requires a target");
        CelExprNode prefix = call.Args[0];

        if (IsStringLiteral(prefix))
        {
            string pattern = EscapeLikePattern(prefix.Constant().StringValue);
            Visit(target);
            _str.Append(" LIKE '").Append(pattern).Append("%'");
            _dialect.WriteLikeEscape(_str);
        }
        else
        {
            // Dynamic pattern: use POSITION
            _str.Append("POSITION(");
            Visit(prefix);
            _str.Append(" IN ");
            Visit(target);
            _str.Append(") = 1");
        }
    }

    /// <summary>Converts str.endsWith(suffix) to str LIKE '%suffix' ESCAPE.</summary>
    private void CallEndsWith(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        CelExprNode target = call.Target
                ?? throw ConversionException.Of(ErrorMessages.InvalidArguments, "endsWith() requires a target");
        CelExprNode suffix = call.Args[0];

        if (IsStringLiteral(suffix))
        {
            string pattern = EscapeLikePattern(suffix.Constant().StringValue);
            Visit(target);
            _str.Append(" LIKE '%").Append(pattern).Append("'");
            _dialect.WriteLikeEscape(_str);
        }
        else
        {
            // Dynamic pattern: use RIGHT() comparison
            _str.Append("RIGHT(");
            Visit(target);
            _str.Append(", LENGTH(");
            Visit(suffix);
            _str.Append(")) = ");
            Visit(suffix);
        }
    }

    /// <summary>Converts str.matches(regex) to dialect-specific regex match.</summary>
    private void CallMatches(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        CelExprNode target = call.Target
                ?? throw ConversionException.Of(ErrorMessages.InvalidArguments, "matches() requires a target");
        CelExprNode pattern = call.Args[0];

        if (!_dialect.SupportsRegex)
        {
            throw ConversionException.Of(
                    ErrorMessages.UnsupportedExpression,
                    "regex matching is not supported by dialect " + _dialect.Name);
        }

        if (IsStringLiteral(pattern))
        {
            string re2Pattern = pattern.Constant().StringValue;
            RegexResult result = _dialect.ConvertRegex(re2Pattern);
            _dialect.WriteRegexMatch(_str, () => Visit(target), result.Pattern, result.CaseInsensitive);
        }
        else
        {
            // Dynamic regex: use ~ operator directly
            Visit(target);
            _str.Append(" ~ ");
            Visit(pattern);
        }
    }

    /// <summary>Converts size(x) or x.size() to LENGTH(x) for strings or ARRAY_LENGTH for arrays.</summary>
    private void CallSize(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        CelExprNode target;
        if (call.Target != null)
        {
            target = call.Target;
        }
        else if (call.Args.Count != 0)
        {
            target = call.Args[0];
        }
        else
        {
            throw ConversionException.Of(ErrorMessages.InvalidArguments, "size() requires an argument");
        }

        CelTypeRef? targetType = GetType(target);

        // JSON array size
        if (ShouldUseJSONPath(target))
        {
            TableAndField? tf = GetTableAndFieldFromSelectChain(target);
            if (tf != null)
            {
                FieldSchema? fieldSchema = FindFieldSchema(tf.Table, tf.Field);
                if (fieldSchema != null && (fieldSchema.IsJson || fieldSchema.IsJsonb))
                {
                    if (IsJSONArrayField(target))
                    {
                        _dialect.WriteJsonArrayLength(_str, () => Visit(target));
                        return;
                    }
                }
            }
        }

        // Array size
        if (IsListType(targetType))
        {
            int dimension = GetArrayDimension(target);
            _dialect.WriteArrayLength(_str, dimension, () => Visit(target));
            return;
        }

        // Map size
        if (IsMapType(targetType))
        {
            // For maps/JSON objects: use json_object_keys count
            _str.Append("(SELECT COUNT(*) FROM jsonb_object_keys(");
            Visit(target);
            _str.Append("))");
            return;
        }

        // String length
        _str.Append("LENGTH(");
        Visit(target);
        _str.Append(')');
    }

    /// <summary>Converts str.lowerAscii() to LOWER(str).</summary>
    private void CallLowerAscii(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        CelExprNode target = call.Target
                ?? throw ConversionException.Of(ErrorMessages.InvalidArguments, "lowerAscii() requires a target");
        _str.Append("LOWER(");
        Visit(target);
        _str.Append(')');
    }

    /// <summary>Converts str.upperAscii() to UPPER(str).</summary>
    private void CallUpperAscii(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        CelExprNode target = call.Target
                ?? throw ConversionException.Of(ErrorMessages.InvalidArguments, "upperAscii() requires a target");
        _str.Append("UPPER(");
        Visit(target);
        _str.Append(')');
    }

    /// <summary>Converts str.trim() to TRIM(str).</summary>
    private void CallTrim(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        CelExprNode target = call.Target
                ?? throw ConversionException.Of(ErrorMessages.InvalidArguments, "trim() requires a target");
        _str.Append("TRIM(");
        Visit(target);
        _str.Append(')');
    }

    /// <summary>Converts str.charAt(idx) to SUBSTRING(str, idx+1, 1).</summary>
    private void CallCharAt(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        CelExprNode target = call.Target
                ?? throw ConversionException.Of(ErrorMessages.InvalidArguments, "charAt() requires a target");
        CelExprNode index = call.Args[0];

        _str.Append("SUBSTRING(");
        Visit(target);
        _str.Append(", ");
        if (index.Kind == CelExprKind.Constant && index.Constant().Kind == CelConstantKind.Int64Value)
        {
            _str.Append(index.Constant().Int64Value + 1);
        }
        else
        {
            Visit(index);
            _str.Append(" + 1");
        }
        _str.Append(", 1)");
    }

    /// <summary>
    /// Converts str.indexOf(substr) to a CASE WHEN POSITION expression.
    /// Returns -1 if not found, otherwise the 0-based index.
    /// </summary>
    private void CallIndexOf(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        CelExprNode target = call.Target
                ?? throw ConversionException.Of(ErrorMessages.InvalidArguments, "indexOf() requires a target");
        CelExprNode needle = call.Args[0];

        if (call.Args.Count == 1)
        {
            // indexOf(substr) with no offset
            _str.Append("CASE WHEN POSITION(");
            Visit(needle);
            _str.Append(" IN ");
            Visit(target);
            _str.Append(") > 0 THEN POSITION(");
            Visit(needle);
            _str.Append(" IN ");
            Visit(target);
            _str.Append(") - 1 ELSE -1 END");
        }
        else
        {
            // indexOf(substr, offset)
            CelExprNode offset = call.Args[1];
            _str.Append("CASE WHEN POSITION(");
            Visit(needle);
            _str.Append(" IN SUBSTRING(");
            Visit(target);
            _str.Append(", ");
            Visit(offset);
            _str.Append(" + 1)) > 0 THEN POSITION(");
            Visit(needle);
            _str.Append(" IN SUBSTRING(");
            Visit(target);
            _str.Append(", ");
            Visit(offset);
            _str.Append(" + 1)) + ");
            Visit(offset);
            _str.Append(" - 1 ELSE -1 END");
        }
    }

    /// <summary>Converts str.lastIndexOf(substr) using REVERSE and POSITION.</summary>
    private void CallLastIndexOf(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        CelExprNode target = call.Target
                ?? throw ConversionException.Of(ErrorMessages.InvalidArguments, "lastIndexOf() requires a target");
        CelExprNode needle = call.Args[0];

        if (call.Args.Count == 1)
        {
            // lastIndexOf(substr) with no offset
            _str.Append("CASE WHEN POSITION(REVERSE(");
            Visit(needle);
            _str.Append(") IN REVERSE(");
            Visit(target);
            _str.Append(")) > 0 THEN LENGTH(");
            Visit(target);
            _str.Append(") - POSITION(REVERSE(");
            Visit(needle);
            _str.Append(") IN REVERSE(");
            Visit(target);
            _str.Append(")) - LENGTH(");
            Visit(needle);
            _str.Append(") + 1 ELSE -1 END");
        }
        else
        {
            // lastIndexOf(substr, offset) - search only in target[0:offset]
            CelExprNode offset = call.Args[1];
            _str.Append("CASE WHEN POSITION(REVERSE(");
            Visit(needle);
            _str.Append(") IN REVERSE(SUBSTRING(");
            Visit(target);
            _str.Append(", 1, ");
            Visit(offset);
            _str.Append("))) > 0 THEN ");
            Visit(offset);
            _str.Append(" - POSITION(REVERSE(");
            Visit(needle);
            _str.Append(") IN REVERSE(SUBSTRING(");
            Visit(target);
            _str.Append(", 1, ");
            Visit(offset);
            _str.Append("))) - LENGTH(");
            Visit(needle);
            _str.Append(") + 1 ELSE -1 END");
        }
    }

    /// <summary>
    /// Converts str.substring(start) or str.substring(start, end) to SUBSTRING.
    /// CEL uses 0-based indexing; SQL uses 1-based.
    /// </summary>
    private void CallSubstring(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        CelExprNode target = call.Target
                ?? throw ConversionException.Of(ErrorMessages.InvalidArguments, "substring() requires a target");
        CelExprNode start = call.Args[0];

        _str.Append("SUBSTRING(");
        Visit(target);
        _str.Append(", ");

        if (start.Kind == CelExprKind.Constant && start.Constant().Kind == CelConstantKind.Int64Value)
        {
            _str.Append(start.Constant().Int64Value + 1);
        }
        else
        {
            Visit(start);
            _str.Append(" + 1");
        }

        if (call.Args.Count > 1)
        {
            CelExprNode end = call.Args[1];
            _str.Append(", ");
            // Length = end - start
            if (start.Kind == CelExprKind.Constant && start.Constant().Kind == CelConstantKind.Int64Value
                    && end.Kind == CelExprKind.Constant && end.Constant().Kind == CelConstantKind.Int64Value)
            {
                _str.Append(end.Constant().Int64Value - start.Constant().Int64Value);
            }
            else
            {
                Visit(end);
                _str.Append(" - ");
                Visit(start);
            }
        }

        _str.Append(')');
    }

    /// <summary>Converts str.replace(old, new) to REPLACE(str, old, new).</summary>
    private void CallReplace(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        CelExprNode target = call.Target
                ?? throw ConversionException.Of(ErrorMessages.InvalidArguments, "replace() requires a target");

        if (call.Args.Count < 2)
        {
            throw ConversionException.Of(ErrorMessages.InvalidArguments,
                    "replace() requires two arguments (old, new)");
        }

        CelExprNode oldStr = call.Args[0];
        CelExprNode newStr = call.Args[1];

        // Check for optional maxReplacements argument
        if (call.Args.Count > 2)
        {
            CelExprNode maxReplacements = call.Args[2];
            if (maxReplacements.Kind == CelExprKind.Constant
                    && maxReplacements.Constant().Kind == CelConstantKind.Int64Value
                    && maxReplacements.Constant().Int64Value == -1)
            {
                // -1 means replace all, which is the default
                _str.Append("REPLACE(");
                Visit(target);
                _str.Append(", ");
                Visit(oldStr);
                _str.Append(", ");
                Visit(newStr);
                _str.Append(')');
                return;
            }
            // Limited replacement not easily supported in SQL, fall through to REPLACE
        }

        _str.Append("REPLACE(");
        Visit(target);
        _str.Append(", ");
        Visit(oldStr);
        _str.Append(", ");
        Visit(newStr);
        _str.Append(')');
    }

    /// <summary>Converts str.reverse() to REVERSE(str).</summary>
    private void CallReverse(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        CelExprNode target;
        if (call.Target != null)
        {
            target = call.Target;
        }
        else if (call.Args.Count != 0)
        {
            target = call.Args[0];
        }
        else
        {
            throw ConversionException.Of(ErrorMessages.InvalidArguments, "reverse() requires a target");
        }

        _str.Append("REVERSE(");
        Visit(target);
        _str.Append(')');
    }

    /// <summary>
    /// Converts str.split(delim) to STRING_TO_ARRAY(str, delim).
    /// With limit: (STRING_TO_ARRAY(str, delim))[1:limit]
    /// </summary>
    private void CallSplit(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        CelExprNode target = call.Target
                ?? throw ConversionException.Of(ErrorMessages.InvalidArguments, "split() requires a target");
        CelExprNode delim = call.Args[0];

        if (call.Args.Count > 1)
        {
            CelExprNode limit = call.Args[1];
            if (limit.Kind == CelExprKind.Constant && limit.Constant().Kind == CelConstantKind.Int64Value)
            {
                long limitVal = limit.Constant().Int64Value;
                _dialect.WriteSplitWithLimit(_str,
                        () => Visit(target),
                        () => Visit(delim),
                        limitVal);
                return;
            }
            // Dynamic limit: just use split without limit
        }

        _dialect.WriteSplit(_str,
                () => Visit(target),
                () => Visit(delim));
    }

    /// <summary>Converts list.join() or list.join(delim) to ARRAY_TO_STRING.</summary>
    private void CallJoin(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        CelExprNode target = call.Target
                ?? throw ConversionException.Of(ErrorMessages.InvalidArguments, "join() requires a target");

        if (call.Args.Count == 0)
        {
            _dialect.WriteJoin(_str, () => Visit(target), null!);
        }
        else
        {
            CelExprNode delim = call.Args[0];
            _dialect.WriteJoin(_str, () => Visit(target), () => Visit(delim));
        }
    }

    /// <summary>
    /// Converts <c>"fmt".format([args...])</c> to the dialect's format function.
    /// Mirrors upstream cel2sql: format string must be a constant, args must be a
    /// list literal, only %s/%d/%f are supported, length is bounded.
    /// </summary>
    private void CallFormat(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        CelExprNode formatExpr;
        CelExprNode argsExpr;
        if (call.Target != null)
        {
            // Member form: "fmt".format(argsList) — exactly one argument expected.
            formatExpr = call.Target;
            if (call.Args.Count != 1)
            {
                throw ConversionException.Of(ErrorMessages.InvalidArguments,
                        "format() requires exactly one argument list, got " + call.Args.Count);
            }
            argsExpr = call.Args[0];
        }
        else if (call.Args.Count == 2)
        {
            // Free form: format(fmt, argsList) — exactly two arguments expected.
            formatExpr = call.Args[0];
            argsExpr = call.Args[1];
        }
        else
        {
            throw ConversionException.Of(ErrorMessages.InvalidArguments,
                    "format() requires a format string and exactly one arguments list");
        }

        if (!IsStringLiteral(formatExpr))
        {
            throw ConversionException.Of(ErrorMessages.UnsupportedExpression,
                    "format() requires a constant format string");
        }
        string formatString = formatExpr.Constant().StringValue;
        if (formatString.Length > MAX_FORMAT_STRING_LENGTH)
        {
            throw ConversionException.Of(ErrorMessages.InvalidArguments,
                    "format() format string exceeds maximum length of " + MAX_FORMAT_STRING_LENGTH);
        }

        // Validate that every '%' begins a supported specifier (%%, %s, %d, %f).
        int placeholderCount = CountAndValidateSpecifiers(formatString);

        if (argsExpr.Kind != CelExprKind.List)
        {
            throw ConversionException.Of(ErrorMessages.UnsupportedExpression,
                    "format() requires a constant list of arguments");
        }
        IReadOnlyList<CelExprNode> argElements = argsExpr.List().Elements;
        if (argElements.Count != placeholderCount)
        {
            throw ConversionException.Of(ErrorMessages.InvalidArguments,
                    "format() argument count mismatch: format has " + placeholderCount
                            + " placeholders but got " + argElements.Count + " arguments");
        }

        var writers = new List<SqlWriter>(argElements.Count);
        foreach (CelExprNode arg in argElements)
        {
            CelExprNode captured = arg;
            writers.Add(() => Visit(captured));
        }
        _dialect.WriteFormat(_str, formatString, writers);
    }

    private static int CountAndValidateSpecifiers(string fmt)
    {
        int count = 0;
        for (int i = 0; i < fmt.Length; i++)
        {
            char c = fmt[i];
            if (c != '%') continue;
            if (i + 1 >= fmt.Length)
            {
                throw ConversionException.Of(ErrorMessages.InvalidArguments,
                        "format() format string ends with '%'");
            }
            char next = fmt[i + 1];
            if (next == '%')
            {
                i++;  // literal percent
                continue;
            }
            if (next != 's' && next != 'd' && next != 'f')
            {
                throw ConversionException.Of(ErrorMessages.InvalidArguments,
                        "format() unsupported specifier '%" + next + "': only %s, %d, %f are allowed");
            }
            count++;
            i++;
        }
        return count;
    }

    // ========================================================================
    // Type Casting Functions
    // ========================================================================

    /// <summary>
    /// Converts int(x), string(x), double(x), etc. to CAST(x AS TYPE).
    /// Special handling for int(timestamp) -> EXTRACT(EPOCH FROM x).
    /// </summary>
    private void CallCasting(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        string fun = call.Function;
        CelExprNode arg = call.Args[0];

        // Special case: int(timestamp) -> extract epoch
        if (TYPE_CONVERT_INT == fun || TYPE_CONVERT_UINT == fun)
        {
            CelTypeRef? argType = GetType(arg);
            if (IsTimestampType(argType))
            {
                _dialect.WriteEpochExtract(_str, () => Visit(arg));
                return;
            }
        }

        _str.Append("CAST(");
        Visit(arg);
        _str.Append(" AS ");
        _dialect.WriteTypeName(_str, fun);
        _str.Append(')');
    }

    // ========================================================================
    // Duration Functions
    // ========================================================================

    /// <summary>
    /// Converts duration("10s") to INTERVAL 10 SECOND.
    /// Parses Go-style duration strings: h, m, s, ms, us, ns.
    /// </summary>
    private void CallDuration(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        CelExprNode arg = call.Args[0];

        if (!IsStringLiteral(arg))
        {
            throw ConversionException.Of(ErrorMessages.InvalidDuration,
                    "duration() requires a string literal argument");
        }

        string durStr = arg.Constant().StringValue;
        DurationComponents dc = ParseDurationString(durStr);

        _dialect.WriteDuration(_str, dc.Value, dc.Unit);
    }

    /// <summary>Represents parsed duration components.</summary>
    private readonly record struct DurationComponents(long Value, string Unit);

    /// <summary>
    /// Parses a Go-style duration string like "10s", "1h30m", "500ms".
    /// Converts to the largest unit that divides evenly.
    /// </summary>
    private DurationComponents ParseDurationString(string durStr)
    {
        if (string.IsNullOrEmpty(durStr))
        {
            throw ConversionException.Of(ErrorMessages.InvalidDuration,
                    "empty duration string");
        }

        // Handle negative durations
        bool negative = false;
        string input = durStr;
        if (input.StartsWith("-"))
        {
            negative = true;
            input = input.Substring(1);
        }

        long totalNanos = 0;
        bool found = false;

        foreach (Match match in DURATION_UNIT_PATTERN.Matches(input))
        {
            found = true;
            long val = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            string unit = match.Groups[2].Value;
            switch (unit)
            {
                case "h": totalNanos += val * 3_600_000_000_000L; break;
                case "m": totalNanos += val * 60_000_000_000L; break;
                case "s": totalNanos += val * 1_000_000_000L; break;
                case "ms": totalNanos += val * 1_000_000L; break;
                case "us": totalNanos += val * 1_000L; break;
                case "ns": totalNanos += val; break;
            }
        }

        if (!found)
        {
            throw ConversionException.Of(ErrorMessages.InvalidDuration,
                    "cannot parse duration: " + durStr);
        }

        if (negative)
        {
            totalNanos = -totalNanos;
        }

        // Find the best unit
        if (totalNanos % 3_600_000_000_000L == 0)
        {
            return new DurationComponents(totalNanos / 3_600_000_000_000L, "HOUR");
        }
        if (totalNanos % 60_000_000_000L == 0)
        {
            return new DurationComponents(totalNanos / 60_000_000_000L, "MINUTE");
        }
        if (totalNanos % 1_000_000_000L == 0)
        {
            return new DurationComponents(totalNanos / 1_000_000_000L, "SECOND");
        }
        if (totalNanos % 1_000_000L == 0)
        {
            return new DurationComponents(totalNanos / 1_000_000L, "MILLISECOND");
        }
        if (totalNanos % 1_000L == 0)
        {
            return new DurationComponents(totalNanos / 1_000L, "MICROSECOND");
        }

        // Fallback to seconds with fractional
        double seconds = totalNanos / 1_000_000_000.0;
        return new DurationComponents((long)seconds, "SECOND");
    }

    // ========================================================================
    // Timestamp Functions
    // ========================================================================

    /// <summary>Converts timestamp("2024-01-01T00:00:00Z") to CAST(expr AS TIMESTAMP WITH TIME ZONE).</summary>
    private void CallTimestampFromString(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        CelExprNode arg = call.Args[0];

        _dialect.WriteTimestampCast(_str, () => Visit(arg));
    }

    /// <summary>Converts timestamp +/- duration to dialect timestamp arithmetic.</summary>
    private void CallTimestampOperation(CelExprNode timestamp, CelExprNode duration, string op)
    {
        _dialect.WriteTimestampArithmetic(_str, op,
                () => Visit(timestamp),
                () => Visit(duration));
    }

    /// <summary>Converts getFullYear(), getMonth(), etc. to EXTRACT(PART FROM expr).</summary>
    private void CallExtractFromTimestamp(CelExprNode expr)
    {
        CelCallNode call = expr.Call();
        string fun = call.Function;
        CelExprNode target = call.Target
                ?? throw ConversionException.Of(ErrorMessages.InvalidArguments,
                        fun + "() requires a target");

        string part = MapTimeFunctionToPart(fun);

        // Check for timezone argument
        if (call.Args.Count != 0)
        {
            CelExprNode tz = call.Args[0];
            _dialect.WriteExtract(_str, part, () => Visit(target), () => Visit(tz));
        }
        else
        {
            _dialect.WriteExtract(_str, part, () => Visit(target), null!);
        }
    }

    /// <summary>Maps CEL time function names to SQL EXTRACT parts.</summary>
    private static string MapTimeFunctionToPart(string fun)
    {
        return fun switch
        {
            TIME_GET_FULL_YEAR => "YEAR",
            TIME_GET_MONTH => "MONTH",
            TIME_GET_DATE or TIME_GET_DAY_OF_MONTH => "DAY",
            TIME_GET_HOURS => "HOUR",
            TIME_GET_MINUTES => "MINUTE",
            TIME_GET_SECONDS => "SECOND",
            TIME_GET_MILLISECONDS => "MILLISECOND",
            TIME_GET_DAY_OF_YEAR => "DOY",
            TIME_GET_DAY_OF_WEEK => "DOW",
            _ => throw ConversionException.Of(
                    ErrorMessages.InvalidTimestampOp,
                    "unsupported time function: " + fun),
        };
    }

    // ========================================================================
    // VisitList - List (Array) Literals
    // ========================================================================

    /// <summary>Converts a CEL list literal [a, b, c] to SQL ARRAY[a, b, c].</summary>
    private void VisitList(CelExprNode expr)
    {
        CelListNode list = expr.List();
        IReadOnlyList<CelExprNode> elements = list.Elements;

        if (elements.Count == 0)
        {
            // Empty list: need to check type for typed empty array
            CelTypeRef? type = GetType(expr);
            if (IsListType(type) && type!.Kind == CelTypeKind.List && type.HasElemType)
            {
                string elemTypeName = CelTypeToSqlTypeName(type.ElemType);
                _dialect.WriteEmptyTypedArray(_str, elemTypeName);
            }
            else
            {
                _dialect.WriteArrayLiteralOpen(_str);
                _dialect.WriteArrayLiteralClose(_str);
            }
            return;
        }

        _dialect.WriteArrayLiteralOpen(_str);
        for (int i = 0; i < elements.Count; i++)
        {
            if (i > 0)
            {
                _str.Append(", ");
            }
            Visit(elements[i]);
        }
        _dialect.WriteArrayLiteralClose(_str);
    }

    /// <summary>Maps a CelType to a SQL type name for empty array creation.</summary>
    private static string CelTypeToSqlTypeName(CelTypeRef? type)
    {
        if (type == null) return "TEXT";
        return type.Kind switch
        {
            CelTypeKind.Bool => "BOOLEAN",
            CelTypeKind.Int => "BIGINT",
            CelTypeKind.Uint => "BIGINT",
            CelTypeKind.Double => "DOUBLE PRECISION",
            CelTypeKind.String => "TEXT",
            CelTypeKind.Bytes => "BYTEA",
            CelTypeKind.Timestamp => "TIMESTAMP WITH TIME ZONE",
            _ => "TEXT",
        };
    }

    // ========================================================================
    // VisitStruct - Struct (Message) Literals
    // ========================================================================

    /// <summary>Converts a struct/message literal to SQL ROW(...).</summary>
    private void VisitStruct(CelExprNode expr)
    {
        CelStructNode s = expr.Struct();
        VisitStructMsg(s);
    }

    /// <summary>Writes a struct message as SQL ROW(field1, field2, ...).</summary>
    private void VisitStructMsg(CelStructNode s)
    {
        _dialect.WriteStructOpen(_str);
        IReadOnlyList<CelStructEntry> entries = s.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            if (i > 0)
            {
                _str.Append(", ");
            }
            Visit(entries[i].Value);
        }
        _dialect.WriteStructClose(_str);
    }

    // ========================================================================
    // VisitStructMap - Map Literals
    // ========================================================================

    /// <summary>
    /// Converts a map literal {k1: v1, k2: v2} to a SQL expression.
    /// For PostgreSQL, this generates jsonb_build_object(k1, v1, k2, v2).
    /// </summary>
    private void VisitStructMap(CelExprNode expr)
    {
        CelMapNode m = expr.Map();
        IReadOnlyList<CelMapEntry> entries = m.Entries;

        if (entries.Count == 0)
        {
            _str.Append("'{}'::jsonb");
            return;
        }

        _str.Append("jsonb_build_object(");
        for (int i = 0; i < entries.Count; i++)
        {
            if (i > 0)
            {
                _str.Append(", ");
            }
            Visit(entries[i].Key);
            _str.Append(", ");
            Visit(entries[i].Value);
        }
        _str.Append(')');
    }

    // ========================================================================
    // VisitComprehension - Comprehensions (all, exists, exists_one, map, filter)
    // ========================================================================

    /// <summary>
    /// Visits a comprehension expression. Identifies the comprehension pattern and
    /// dispatches to the appropriate handler.
    /// </summary>
    private void VisitComprehension(CelExprNode expr)
    {
        _comprehensionDepth++;
        if (_comprehensionDepth > MAX_COMPREHENSION_DEPTH)
        {
            _comprehensionDepth--;
            throw ConversionException.Of(
                    ErrorMessages.ComprehensionDepthExceeded,
                    "comprehension nesting depth " + _comprehensionDepth + " exceeds maximum of " + MAX_COMPREHENSION_DEPTH);
        }

        try
        {
            CelComprehensionNode comp = expr.Comprehension();
            ComprehensionKind kind = IdentifyComprehension(comp);

            switch (kind)
            {
                case ComprehensionKind.ALL: VisitComprehensionAll(comp); break;
                case ComprehensionKind.EXISTS: VisitComprehensionExists(comp); break;
                case ComprehensionKind.EXISTS_ONE: VisitComprehensionExistsOne(comp); break;
                case ComprehensionKind.MAP: VisitComprehensionMap(comp); break;
                case ComprehensionKind.FILTER: VisitComprehensionFilter(comp); break;
                default:
                    throw ConversionException.Of(
                        ErrorMessages.UnsupportedComprehension,
                        "unsupported comprehension pattern");
            }
        }
        finally
        {
            _comprehensionDepth--;
        }
    }

    /// <summary>Enumeration of comprehension kinds.</summary>
    private enum ComprehensionKind
    {
        ALL, EXISTS, EXISTS_ONE, MAP, FILTER, UNKNOWN
    }

    /// <summary>
    /// Identifies the type of comprehension by examining its structure.
    /// Based on the Go cel2sql comprehension pattern matching.
    /// </summary>
    private ComprehensionKind IdentifyComprehension(CelComprehensionNode comp)
    {
        // all: accuInit = true, loopCondition = @not_strictly_false(accuVar) && step, result = accuVar
        // exists: accuInit = false, loopCondition = @not_strictly_false(!accuVar) && step, result = accuVar
        // exists_one: accuInit = 0, step = conditional adding, result = accuVar == 1
        // map: accuInit = [], step = accuVar + [elem], result = accuVar
        // filter: accuInit = [], step = conditional append, result = accuVar

        CelExprNode accuInit = comp.AccuInit;
        CelExprNode result = comp.Result;
        CelExprNode loopStep = comp.LoopStep;

        // Check for all(): accuInit = true, result = accuVar
        if (IsBoolTrue(accuInit) && IsAccuVarRef(result, comp.AccuVar))
        {
            return ComprehensionKind.ALL;
        }

        // Check for exists(): accuInit = false, result = accuVar
        if (IsBoolFalse(accuInit) && IsAccuVarRef(result, comp.AccuVar))
        {
            return ComprehensionKind.EXISTS;
        }

        // Check for exists_one(): accuInit = 0, result = accuVar == 1
        if (IsIntZero(accuInit) && IsAccuEqualsOne(result, comp.AccuVar))
        {
            return ComprehensionKind.EXISTS_ONE;
        }

        // Check for map() or filter(): accuInit = []
        if (IsEmptyList(accuInit))
        {
            if (IsMapStep(loopStep, comp.AccuVar))
            {
                return ComprehensionKind.MAP;
            }
            if (IsFilterStep(loopStep, comp.AccuVar))
            {
                return ComprehensionKind.FILTER;
            }
            // Could also be a map without transform (just the element)
            return ComprehensionKind.MAP;
        }

        return ComprehensionKind.UNKNOWN;
    }

    /// <summary>Checks if an expression is a reference to the accumulator variable.</summary>
    private static bool IsAccuVarRef(CelExprNode expr, string accuVar)
    {
        return expr.Kind == CelExprKind.Ident && expr.Ident().Name == accuVar;
    }

    /// <summary>Checks if result == (accuVar == 1) for exists_one detection.</summary>
    private static bool IsAccuEqualsOne(CelExprNode expr, string accuVar)
    {
        if (expr.Kind != CelExprKind.Call) return false;
        CelCallNode call = expr.Call();
        if (EQUALS != call.Function) return false;
        if (call.Args.Count != 2) return false;
        CelExprNode lhs = call.Args[0];
        CelExprNode rhs = call.Args[1];
        return IsAccuVarRef(lhs, accuVar)
                && rhs.Kind == CelExprKind.Constant
                && rhs.Constant().Kind == CelConstantKind.Int64Value
                && rhs.Constant().Int64Value == 1;
    }

    /// <summary>
    /// Checks if the loop step represents a map comprehension pattern.
    /// Map step: accuVar + [transform_expr] (unconditional)
    /// </summary>
    private static bool IsMapStep(CelExprNode step, string accuVar)
    {
        if (step.Kind != CelExprKind.Call) return false;
        CelCallNode call = step.Call();
        if (ADD != call.Function) return false;
        if (call.Args.Count != 2) return false;
        CelExprNode lhs = call.Args[0];
        CelExprNode rhs = call.Args[1];
        return IsAccuVarRef(lhs, accuVar) && rhs.Kind == CelExprKind.List;
    }

    /// <summary>
    /// Checks if the loop step represents a filter comprehension pattern.
    /// Filter step: conditional(predicate, accuVar + [elem], accuVar)
    /// </summary>
    private static bool IsFilterStep(CelExprNode step, string accuVar)
    {
        if (step.Kind != CelExprKind.Call) return false;
        CelCallNode call = step.Call();
        if (CONDITIONAL != call.Function) return false;
        if (call.Args.Count != 3) return false;
        CelExprNode trueExpr = call.Args[1];
        CelExprNode falseExpr = call.Args[2];
        return IsAccuVarRef(falseExpr, accuVar)
                && trueExpr.Kind == CelExprKind.Call
                && ADD == trueExpr.Call().Function;
    }

    // ========================================================================
    // Comprehension Visitors
    // ========================================================================

    /// <summary>
    /// Converts list.all(x, predicate) to:
    /// NOT EXISTS (SELECT 1 FROM UNNEST(list) AS x WHERE NOT (predicate))
    /// </summary>
    private void VisitComprehensionAll(CelComprehensionNode comp)
    {
        CelExprNode iterRange = comp.IterRange;
        string iterVar = comp.IterVar;
        CelExprNode predicate = ExtractComprehensionPredicate(comp.LoopCondition, comp.LoopStep);

        _dialect.WriteComprehensionNotExists(_str, () =>
        {
            _dialect.WriteComprehensionSource(_str, () => Visit(iterRange), iterVar);
            _str.Append(" WHERE NOT (");
            Visit(predicate);
            _str.Append(')');
        });
    }

    /// <summary>
    /// Converts list.exists(x, predicate) to:
    /// EXISTS (SELECT 1 FROM UNNEST(list) AS x WHERE predicate)
    /// </summary>
    private void VisitComprehensionExists(CelComprehensionNode comp)
    {
        CelExprNode iterRange = comp.IterRange;
        string iterVar = comp.IterVar;
        CelExprNode predicate = ExtractComprehensionPredicate(comp.LoopCondition, comp.LoopStep);

        _dialect.WriteComprehensionExists(_str, () =>
        {
            _dialect.WriteComprehensionSource(_str, () => Visit(iterRange), iterVar);
            _str.Append(" WHERE ");
            Visit(predicate);
        });
    }

    /// <summary>
    /// Converts list.exists_one(x, predicate) to:
    /// (SELECT COUNT(*) FROM UNNEST(list) AS x WHERE predicate) = 1
    /// </summary>
    private void VisitComprehensionExistsOne(CelComprehensionNode comp)
    {
        CelExprNode iterRange = comp.IterRange;
        string iterVar = comp.IterVar;
        CelExprNode predicate = ExtractExistsOnePredicate(comp.LoopStep);

        _str.Append("(SELECT COUNT(*) FROM ");
        _dialect.WriteComprehensionSource(_str, () => Visit(iterRange), iterVar);
        _str.Append(" WHERE ");
        Visit(predicate);
        _str.Append(") = 1");
    }

    /// <summary>
    /// Converts list.map(x, transform) to:
    /// ARRAY(SELECT transform FROM UNNEST(list) AS x)
    /// </summary>
    private void VisitComprehensionMap(CelComprehensionNode comp)
    {
        CelExprNode iterRange = comp.IterRange;
        string iterVar = comp.IterVar;
        CelExprNode transform = ExtractMapTransform(comp.LoopStep, comp.AccuVar);

        _dialect.WriteArraySubqueryOpen(_str);
        Visit(transform);
        _dialect.WriteArraySubqueryExprClose(_str);
        _str.Append(" FROM ");
        _dialect.WriteComprehensionSource(_str, () => Visit(iterRange), iterVar);
        _str.Append(')');
    }

    /// <summary>
    /// Converts list.filter(x, predicate) to:
    /// ARRAY(SELECT x FROM UNNEST(list) AS x WHERE predicate)
    /// </summary>
    private void VisitComprehensionFilter(CelComprehensionNode comp)
    {
        CelExprNode iterRange = comp.IterRange;
        string iterVar = comp.IterVar;
        CelExprNode predicate = ExtractFilterPredicate(comp.LoopStep);

        _dialect.WriteArraySubqueryOpen(_str);
        _str.Append(iterVar);
        _dialect.WriteArraySubqueryExprClose(_str);
        _str.Append(" FROM ");
        _dialect.WriteComprehensionSource(_str, () => Visit(iterRange), iterVar);
        _str.Append(" WHERE ");
        Visit(predicate);
        _str.Append(')');
    }

    // ========================================================================
    // Comprehension Predicate Extraction
    // ========================================================================

    /// <summary>
    /// Extracts the predicate from an all/exists comprehension.
    /// The loopCondition is typically: @not_strictly_false(accuVar) &amp;&amp; step (for all)
    /// or @not_strictly_false(!accuVar) &amp;&amp; step (for exists).
    /// The actual predicate is in the loopStep.
    /// </summary>
    private CelExprNode ExtractComprehensionPredicate(CelExprNode loopCondition, CelExprNode loopStep)
    {
        // For all: step is the predicate (AND'd with current accu)
        // For exists: step is the predicate (OR'd with current accu)
        // The loopStep is typically: accuVar && predicate (all) or accuVar || predicate (exists)
        if (loopStep.Kind == CelExprKind.Call)
        {
            CelCallNode call = loopStep.Call();
            if (LOGICAL_OR == call.Function || LOGICAL_AND == call.Function)
            {
                // The predicate is the second argument (first is the accu var ref)
                if (call.Args.Count == 2)
                {
                    CelExprNode second = call.Args[1];
                    return second;
                }
            }
        }
        return loopStep;
    }

    /// <summary>
    /// Extracts the predicate from an exists_one comprehension step.
    /// Step is: conditional(predicate, accuVar + 1, accuVar)
    /// </summary>
    private CelExprNode ExtractExistsOnePredicate(CelExprNode loopStep)
    {
        if (loopStep.Kind == CelExprKind.Call)
        {
            CelCallNode call = loopStep.Call();
            if (CONDITIONAL == call.Function && call.Args.Count == 3)
            {
                return call.Args[0]; // The condition of the ternary
            }
        }
        return loopStep;
    }

    /// <summary>
    /// Extracts the transform expression from a map comprehension step.
    /// Step is: accuVar + [transform_expr]
    /// </summary>
    private CelExprNode ExtractMapTransform(CelExprNode loopStep, string accuVar)
    {
        if (loopStep.Kind == CelExprKind.Call)
        {
            CelCallNode call = loopStep.Call();
            if (ADD == call.Function && call.Args.Count == 2)
            {
                CelExprNode rhs = call.Args[1];
                if (rhs.Kind == CelExprKind.List)
                {
                    CelListNode list = rhs.List();
                    if (list.Elements.Count != 0)
                    {
                        return list.Elements[0];
                    }
                }
            }
            // Filter step wrapped in conditional
            if (CONDITIONAL == call.Function && call.Args.Count == 3)
            {
                CelExprNode trueExpr = call.Args[1];
                return ExtractMapTransform(trueExpr, accuVar);
            }
        }
        return loopStep;
    }

    /// <summary>
    /// Extracts the predicate from a filter comprehension step.
    /// Step is: conditional(predicate, accuVar + [iterVar], accuVar)
    /// </summary>
    private CelExprNode ExtractFilterPredicate(CelExprNode loopStep)
    {
        if (loopStep.Kind == CelExprKind.Call)
        {
            CelCallNode call = loopStep.Call();
            if (CONDITIONAL == call.Function && call.Args.Count == 3)
            {
                return call.Args[0];
            }
        }
        return loopStep;
    }

    // ========================================================================
    // JSON Helpers
    // ========================================================================

    /// <summary>
    /// Determines if a select expression should use JSON path access.
    /// Returns true when:
    /// <list type="bullet">
    ///   <item>the chain's root identifier was declared as a flat JSONB variable
    ///       via <c>ConvertOptions.WithJsonVariables(string...)</c>, or</item>
    ///   <item>the expression accesses a field declared as JSON/JSONB in the schema.</item>
    /// </list>
    /// </summary>
    private bool ShouldUseJSONPath(CelExprNode expr)
    {
        // Flat JSONB variables: any access whose root ident is in jsonVariables.
        // Covers both dot-notation (SELECT) and bracket-notation (CALL with @index).
        string? root = GetRootIdentName(expr);
        if (root != null && _jsonVariables.Contains(root))
        {
            return true;
        }
        if (_schemas == null || _schemas.Count == 0) return false;
        if (expr.Kind != CelExprKind.Select) return false;

        // Walk up the chain to find the root field
        TableAndField? tf = GetTableAndFieldFromSelectChain(expr);
        if (tf == null) return false;

        FieldSchema? fieldSchema = FindFieldSchema(tf.Table, tf.Field);
        return fieldSchema != null && fieldSchema.IsJson;
    }

    /// <summary>
    /// Walks the access chain (SELECT operand or @index args[0]) up to the
    /// root identifier and returns its name, or null if the root isn't an ident.
    /// </summary>
    private string? GetRootIdentName(CelExprNode expr)
    {
        CelExprNode? cur = expr;
        while (cur != null)
        {
            switch (cur.Kind)
            {
                case CelExprKind.Ident:
                    return cur.Ident().Name;
                case CelExprKind.Select:
                    cur = cur.Select().Operand;
                    break;
                case CelExprKind.Call:
                {
                    CelCallNode call = cur.Call();
                    if (INDEX == call.Function && call.Args.Count != 0)
                    {
                        cur = call.Args[0];
                    }
                    else
                    {
                        return null;
                    }
                    break;
                }
                default:
                    return null;
            }
        }
        return null;
    }

    /// <summary>Checks if the expression chain contains a JSON field access.</summary>
    private bool HasJSONFieldInChain(CelExprNode expr)
    {
        // Flat JSONB variable at the root counts as a JSON field for this check.
        string? root = GetRootIdentName(expr);
        if (root != null && _jsonVariables.Contains(root)) return true;
        if (expr.Kind != CelExprKind.Select) return false;
        if (ShouldUseJSONPath(expr)) return true;
        return HasJSONFieldInChain(expr.Select().Operand);
    }

    /// <summary>
    /// Checks if the JSON extraction is the final (text) extraction.
    /// Used to determine whether to use -> (JSON) or ->> (text) operator.
    /// </summary>
    private bool IsJSONTextExtraction(CelExprNode expr)
    {
        // If this expression's result is used in a comparison or string context, extract as text
        // For now, the default is to extract as text (->>) for the final access
        // A non-final access (intermediate) uses -> to preserve JSON type
        CelTypeRef? type = GetType(expr);
        return type != null && type.Kind != CelTypeKind.Map && type.Kind != CelTypeKind.List;
    }

    /// <summary>Checks if a field in a select chain is a JSON array field.</summary>
    private bool IsJSONArrayField(CelExprNode expr)
    {
        TableAndField? tf = GetTableAndFieldFromSelectChain(expr);
        if (tf == null) return false;
        FieldSchema? fieldSchema = FindFieldSchema(tf.Table, tf.Field);
        return fieldSchema != null && (fieldSchema.IsJson || fieldSchema.IsJsonb) && fieldSchema.Repeated;
    }

    /// <summary>Checks if an expression is a JSON object field access.</summary>
    private bool IsJSONObjectFieldAccess(CelExprNode expr)
    {
        if (expr.Kind != CelExprKind.Select) return false;
        return ShouldUseJSONPath(expr.Select().Operand);
    }

    /// <summary>Checks if an expression is a nested JSON access (operand is also a JSON select).</summary>
    private bool IsNestedJSONAccess(CelExprNode expr)
    {
        if (expr.Kind != CelExprKind.Select) return false;
        return ShouldUseJSONPath(expr);
    }

    /// <summary>Gets the JSON array function name based on the JSON type.</summary>
    private static string GetJSONArrayFunction(bool isJSONB, bool asText)
    {
        if (isJSONB)
        {
            return asText ? "jsonb_array_elements_text" : "jsonb_array_elements";
        }
        else
        {
            return asText ? "json_array_elements_text" : "json_array_elements";
        }
    }

    /// <summary>
    /// Builds a JSON path from a nested select chain.
    /// Returns the root expression and populates the path list.
    /// </summary>
    private CelExprNode BuildJSONPathInternal(CelExprNode expr, List<string> path)
    {
        if (expr.Kind != CelExprKind.Select) return expr;
        CelSelectNode sel = expr.Select();
        CelExprNode operand = sel.Operand;

        // Check if operand is the root JSON field
        if (!IsNestedJSONAccess(operand))
        {
            path.Insert(0, sel.Field);
            return operand;
        }

        path.Insert(0, sel.Field);
        return BuildJSONPathInternal(operand, path);
    }

    // ========================================================================
    // Schema Helpers
    // ========================================================================

    /// <summary>Extracts table and field name from a select chain like table.field or field.</summary>
    private TableAndField? GetTableAndFieldFromSelectChain(CelExprNode expr)
    {
        if (expr.Kind == CelExprKind.Select)
        {
            CelSelectNode sel = expr.Select();
            CelExprNode operand = sel.Operand;

            // Check if operand is an ident (direct field access or table.field)
            if (operand.Kind == CelExprKind.Ident)
            {
                string table = operand.Ident().Name;
                string field = sel.Field;
                return new TableAndField(table, field);
            }

            // Walk up the chain to find the root
            if (operand.Kind == CelExprKind.Select)
            {
                return GetTableAndFieldFromSelectChain(operand);
            }
        }

        // For ident expressions, there's no table prefix
        if (expr.Kind == CelExprKind.Ident)
        {
            return new TableAndField("", expr.Ident().Name);
        }

        return null;
    }

    /// <summary>Finds a field schema by table name and field name.</summary>
    private FieldSchema? FindFieldSchema(string table, string field)
    {
        if (_schemas == null) return null;

        // Try table-specific lookup first
        if (table != null && table.Length != 0)
        {
            if (_schemas.TryGetValue(table, out var schema) && schema != null)
            {
                FieldSchema? fs = schema.FindField(field);
                if (fs is not null) return fs;
            }
        }

        // Try all schemas
        foreach (Schema.Schema schema in _schemas.Values)
        {
            FieldSchema? fs = schema.FindField(field);
            if (fs is not null) return fs;
        }

        return null;
    }

    /// <summary>Gets the array dimension for a field.</summary>
    private int GetArrayDimension(CelExprNode expr)
    {
        TableAndField? tf = GetTableAndFieldFromSelectChain(expr);
        if (tf != null)
        {
            FieldSchema? fieldSchema = FindFieldSchema(tf.Table, tf.Field);
            if (fieldSchema != null && fieldSchema.Dimensions > 0)
            {
                return fieldSchema.Dimensions;
            }
        }
        return 1;
    }

    /// <summary>Simple record to hold table and field names from a select chain.</summary>
    private sealed record TableAndField(string Table, string Field);

    // ========================================================================
    // Utility Helpers
    // ========================================================================

    /// <summary>Escapes special characters in a LIKE pattern (%, _, \).</summary>
    private static string EscapeLikePattern(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            switch (c)
            {
                case '%': sb.Append("\\%"); break;
                case '_': sb.Append("\\_"); break;
                case '\\': sb.Append("\\\\"); break;
                case '\'': sb.Append("''"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Gets the string value from a string literal expression.</summary>
    private static string GetStringValue(CelExprNode expr)
    {
        if (IsStringLiteral(expr))
        {
            return expr.Constant().StringValue;
        }
        return "";
    }

    // ========================================================================
    // Parenthesization / Precedence Helpers
    // ========================================================================

    /// <summary>
    /// Visits a child expression, wrapping it in parentheses if needed based on
    /// operator precedence.
    /// </summary>
    private void VisitMaybeNested(CelExprNode parent, CelExprNode child)
    {
        bool needsParens = IsComplexOperatorWithRespectTo(parent, child);
        if (needsParens)
        {
            _str.Append('(');
        }
        Visit(child);
        if (needsParens)
        {
            _str.Append(')');
        }
    }

    /// <summary>
    /// Checks if the child expression is a complex operator that needs parenthesization
    /// with respect to the parent expression.
    /// </summary>
    private bool IsComplexOperatorWithRespectTo(CelExprNode parent, CelExprNode child)
    {
        if (!IsComplexOperator(child)) return false;
        if (!IsComplexOperator(parent)) return false;

        string? parentOp = GetOperator(parent);
        string? childOp = GetOperator(child);

        if (parentOp == null || childOp == null) return false;

        // If same precedence, check for left-recursiveness
        if (IsSamePrecedence(parentOp, childOp))
        {
            // For non-commutative operators, right-hand side needs parens
            return IsLeftRecursive(parent, child);
        }

        // If child has lower precedence than parent, needs parens
        return IsLowerPrecedence(childOp, parentOp);
    }

    /// <summary>Checks if an expression is a complex operator (binary, unary, or ternary).</summary>
    private static bool IsComplexOperator(CelExprNode expr)
    {
        if (expr.Kind != CelExprKind.Call) return false;
        string op = expr.Call().Function;
        return IsBinaryOrTernaryOperator(op) || LOGICAL_NOT == op || NEGATE == op;
    }

    /// <summary>Gets the operator name from a call expression.</summary>
    private static string? GetOperator(CelExprNode expr)
    {
        if (expr.Kind != CelExprKind.Call) return null;
        return expr.Call().Function;
    }

    /// <summary>Checks if two operators have the same precedence.</summary>
    private static bool IsSamePrecedence(string op1, string op2)
    {
        return GetPrecedence(op1) == GetPrecedence(op2);
    }

    /// <summary>Checks if op1 has lower precedence than op2.</summary>
    private static bool IsLowerPrecedence(string op1, string op2)
    {
        return GetPrecedence(op1) > GetPrecedence(op2);
    }

    /// <summary>Gets the precedence level for an operator. Higher number = lower precedence (binds less tightly).</summary>
    private static int GetPrecedence(string op)
    {
        return PRECEDENCE_MAP.TryGetValue(op, out var p) ? p : 0;
    }

    /// <summary>
    /// Checks if a child expression is the right-hand side operand of a binary parent,
    /// making it potentially need parenthesization for left-recursive operators.
    /// </summary>
    private static bool IsLeftRecursive(CelExprNode parent, CelExprNode child)
    {
        if (parent.Kind != CelExprKind.Call) return false;
        CelCallNode call = parent.Call();
        if (call.Args.Count != 2) return false;
        // Child is left-recursive if it's the RHS of the parent
        return call.Args[1].Id == child.Id;
    }

    /// <summary>Checks if an operator name represents a binary or ternary operator.</summary>
    private static bool IsBinaryOrTernaryOperator(string op)
    {
        return op switch
        {
            CONDITIONAL or LOGICAL_AND or LOGICAL_OR or EQUALS or NOT_EQUALS
                or LESS or LESS_EQUALS or GREATER or GREATER_EQUALS
                or ADD or SUBTRACT or MULTIPLY or DIVIDE or MODULO
                or IN or OLD_IN or INDEX => true,
            _ => false,
        };
    }
}
