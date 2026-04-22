namespace NGB.Tools.Exceptions;

public abstract class NgbForbiddenException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null,
    Exception? innerException = null)
    : NgbException(message, errorCode, NgbErrorKind.Forbidden, context, innerException);
