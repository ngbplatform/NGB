namespace NGB.Tools.Exceptions;

public abstract class NgbValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null,
    Exception? innerException = null)
    : NgbException(message, errorCode, NgbErrorKind.Validation, context, innerException);
