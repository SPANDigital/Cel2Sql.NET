namespace Cel2Sql.Dialects;

/// <summary>
/// Delegate for writing SQL fragments. Used as callbacks in <see cref="IDialect"/> methods
/// for writing sub-expressions. The Java equivalent is the functional interface
/// <c>SqlWriter</c> (Go's <c>func() error</c> callback pattern).
/// </summary>
public delegate void SqlWriter();
