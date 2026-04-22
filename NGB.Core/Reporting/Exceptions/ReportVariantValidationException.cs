using NGB.Tools.Exceptions;

namespace NGB.Core.Reporting.Exceptions;

public sealed class ReportVariantValidationException(
    string message,
    string reason,
    IReadOnlyDictionary<string, string[]>? errors = null,
    IReadOnlyDictionary<string, object?>? details = null)
    : NgbValidationException(
        message: message,
        errorCode: Code,
        context: BuildContext(reason, errors, details))
{
    public const string Code = "report.variant.invalid";

    private static IReadOnlyDictionary<string, object?> BuildContext(
        string reason,
        IReadOnlyDictionary<string, string[]>? errors,
        IReadOnlyDictionary<string, object?>? details)
    {
        var context = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["reason"] = reason
        };

        if (errors is not null && errors.Count > 0)
            context["errors"] = errors;

        if (details is not null)
        {
            foreach (var pair in details)
            {
                context[pair.Key] = pair.Value;
            }
        }

        return context;
    }
}
