using NGB.Tools.Exceptions;

namespace NGB.Core.Reporting.Exceptions;

public sealed class ReportLayoutValidationException(
    string message,
    string? fieldPath = null,
    IReadOnlyDictionary<string, string[]>? errors = null,
    IReadOnlyDictionary<string, object?>? context = null,
    Exception? innerException = null)
    : NgbValidationException(
        message,
        Code,
        BuildContext(fieldPath, errors, context),
        innerException)
{
    public const string Code = "report.layout.invalid";

    private static IReadOnlyDictionary<string, object?> BuildContext(
        string? fieldPath,
        IReadOnlyDictionary<string, string[]>? errors,
        IReadOnlyDictionary<string, object?>? context)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (context is not null)
        {
            foreach (var pair in context)
                result[pair.Key] = pair.Value;
        }

        if (!string.IsNullOrWhiteSpace(fieldPath))
            result["fieldPath"] = fieldPath;

        if (errors is not null && errors.Count > 0)
            result["errors"] = errors;

        return result;
    }
}