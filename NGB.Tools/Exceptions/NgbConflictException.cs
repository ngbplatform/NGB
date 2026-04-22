namespace NGB.Tools.Exceptions;

public abstract class NgbConflictException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null,
    Exception? innerException = null)
    : NgbException(message, errorCode, NgbErrorKind.Conflict, context, innerException);
