namespace Cel2Sql.Cel;

/// <summary>The kind of a CEL AST expression node (mirrors dev.cel's CelExpr.ExprKind.Kind).</summary>
public enum CelExprKind
{
    Unspecified,
    Constant,
    Ident,
    Select,
    Call,
    List,
    Struct,
    Map,
    Comprehension,
}

/// <summary>The kind of a CEL constant literal (mirrors dev.cel's CelConstant.Kind).</summary>
public enum CelConstantKind
{
    NullValue,
    BooleanValue,
    Int64Value,
    Uint64Value,
    DoubleValue,
    StringValue,
    BytesValue,
    DurationValue,
    TimestampValue,
}

/// <summary>The kind of a checked CEL type (mirrors the subset of dev.cel's CelKind the converter needs).</summary>
public enum CelTypeKind
{
    Unknown,
    Bool,
    Int,
    Uint,
    Double,
    String,
    Bytes,
    List,
    Map,
    Timestamp,
    Duration,
    Null,
    Dyn,
}
