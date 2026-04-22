namespace NGB.Tools.Exceptions;

public abstract class NgbConfigurationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null,
    Exception? innerException = null)
    : NgbException(message, errorCode, NgbErrorKind.Configuration, context, innerException);
