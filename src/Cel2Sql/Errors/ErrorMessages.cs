namespace Cel2Sql.Errors;

/// <summary>
/// Centralized error message constants for CEL to SQL conversion.
/// These provide sanitized, user-safe messages that do not leak internal details.
/// </summary>
public static class ErrorMessages
{
    public const string UnsupportedExpression = "Unsupported expression type";
    public const string InvalidOperator = "Invalid operator in expression";
    public const string UnsupportedType = "Unsupported type in expression";
    public const string UnsupportedComprehension = "Unsupported comprehension operation";
    public const string ComprehensionDepthExceeded = "Comprehension nesting exceeds maximum depth";
    public const string InvalidFieldAccess = "Invalid field access in expression";
    public const string ConversionFailed = "Failed to convert expression component";
    public const string InvalidTimestampOp = "Invalid timestamp operation";
    public const string InvalidDuration = "Invalid duration value";
    public const string InvalidArguments = "Invalid function arguments";
    public const string InvalidPattern = "Invalid pattern in expression";
}
