namespace Cel2Sql.Errors;

/// <summary>
/// Represents an error that occurred during CEL to SQL conversion.
/// Provides a sanitized user-facing message while preserving detailed information
/// for logging and debugging. This prevents information disclosure through error messages
/// (CWE-209: Information Exposure Through Error Message).
/// </summary>
public class ConversionException : Exception
{
    private readonly string _internalDetails;

    /// <summary>Gets the sanitized user-facing error message.</summary>
    public string UserMessage { get; }

    /// <summary>
    /// Gets the full internal details for logging purposes.
    /// This should only be used with structured logging, never displayed to users.
    /// </summary>
    public string InternalDetails =>
        !string.IsNullOrEmpty(_internalDetails) ? _internalDetails : UserMessage;

    public ConversionException(string userMessage, string internalDetails)
        : base(userMessage)
    {
        UserMessage = userMessage;
        _internalDetails = internalDetails;
    }

    public ConversionException(string userMessage, string internalDetails, Exception? cause)
        : base(userMessage, cause)
    {
        UserMessage = userMessage;
        _internalDetails = internalDetails;
    }

    /// <summary>Creates a ConversionException with separate user and internal messages.</summary>
    public static ConversionException Of(string userMessage, string internalDetails) =>
        new(userMessage, internalDetails);

    /// <summary>Creates a ConversionException with separate user and internal messages, wrapping a cause.</summary>
    public static ConversionException Of(string userMessage, string internalDetails, Exception? cause) =>
        new(userMessage, internalDetails, cause);

    /// <summary>
    /// Wraps an existing exception with additional internal context.
    /// Preserves specific user messages through wrapping chains: if the cause is
    /// a ConversionException with a non-generic user message, that message is preserved.
    /// </summary>
    public static ConversionException Wrap(Exception cause, string internalContext)
    {
        if (cause is ConversionException ce)
        {
            string details = internalContext.Length == 0
                ? ce.InternalDetails
                : internalContext + ": " + ce.InternalDetails;
            if (ce.UserMessage != ErrorMessages.ConversionFailed)
            {
                return new ConversionException(ce.UserMessage, details, cause);
            }
            return new ConversionException(ErrorMessages.ConversionFailed, details, cause);
        }

        string d = internalContext.Length == 0
            ? cause.Message
            : internalContext + ": " + cause.Message;
        return new ConversionException(cause.Message, d, cause);
    }
}
