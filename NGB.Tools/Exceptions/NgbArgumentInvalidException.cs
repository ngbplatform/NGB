namespace NGB.Tools.Exceptions;

/// <summary>
/// Strict alternative to <see cref="ArgumentException"/>.
/// Use when an argument/value is present but invalid.
/// </summary>
public sealed class NgbArgumentInvalidException(string paramName, string reason) : NgbValidationException(
    message: reason,
    errorCode: Code,
    context: new Dictionary<string, object?>
    {
        ["paramName"] = paramName,
        ["reason"] = reason
    })
{
    public const string Code = "ngb.validation.invalid_argument";

    public string ParamName { get; } = paramName;

    public string Reason { get; } = reason;
}
