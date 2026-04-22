namespace NGB.Tools.Exceptions;

/// <summary>
/// Strict alternative to <see cref="ArgumentOutOfRangeException"/>.
/// Use when an argument value is outside the supported/allowed range.
/// </summary>
public sealed class NgbArgumentOutOfRangeException(string paramName, object? actualValue, string reason)
    : NgbValidationException(
        message: BuildMessage(paramName, reason),
        errorCode: Code,
        context: new Dictionary<string, object?>
        {
            ["paramName"] = ValidateParamName(paramName),
            ["actualValue"] = actualValue,
            ["reason"] = reason ?? string.Empty
        })
{
    public const string Code = "ngb.validation.argument_out_of_range";

    public string ParamName { get; } = ValidateParamName(paramName);

    public object? ActualValue { get; } = actualValue;

    public string Reason { get; } = reason;

    private static string BuildMessage(string paramName, string reason)
    {
        var validated = ValidateParamName(paramName);
        var label = NgbArgumentLabelFormatter.Format(validated);
        if (string.IsNullOrWhiteSpace(reason))
            return $"{label} is out of range.";

        var trimmedReason = reason.Trim();
        if (char.IsLower(trimmedReason[0]))
            trimmedReason = char.ToUpperInvariant(trimmedReason[0]) + trimmedReason[1..];

        return $"{label} is out of range. {trimmedReason}";
    }

    private static string ValidateParamName(string paramName)
    {
        if (string.IsNullOrWhiteSpace(paramName))
            throw new NgbArgumentRequiredException(nameof(paramName));

        return paramName;
    }
}
