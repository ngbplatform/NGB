namespace NGB.Tools.Exceptions;

/// <summary>
/// Strict alternative to <see cref="ArgumentNullException"/> and "required" <see cref="ArgumentException"/>.
/// Use when a required argument/value is missing.
/// </summary>
public sealed class NgbArgumentRequiredException(string paramName) : NgbValidationException(
    message: $"{NgbArgumentLabelFormatter.Format(paramName)} is required.",
    errorCode: Code,
    context: new Dictionary<string, object?>
    {
        ["paramName"] = paramName
    })
{
    public const string Code = "ngb.validation.required";

    public string ParamName { get; } = paramName;
}
