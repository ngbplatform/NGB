namespace NGB.Tools.Exceptions;

public abstract class NgbNotFoundException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null,
    Exception? innerException = null)
    : NgbException(message, errorCode, NgbErrorKind.NotFound, context, innerException);
