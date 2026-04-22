using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class PropertyManagementReportValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbValidationException(message, errorCode, context)
{
    public static PropertyManagementReportValidationException Required(string reportCode, string field)
    {
        var message = $"{PropertyManagementValidationLabels.Label(field)} is required.";
        return new(
            message,
            "pm.validation.report.parameter.required",
            BuildContext(reportCode, field, message));
    }

    public static PropertyManagementReportValidationException InvalidGuid(string reportCode, string field)
    {
        var message = $"Select a valid {PropertyManagementValidationLabels.Label(field)}.";
        return new(
            message,
            "pm.validation.report.parameter.invalid_guid",
            BuildContext(reportCode, field, message));
    }

    public static PropertyManagementReportValidationException InvalidDate(string reportCode, string field)
    {
        var message = $"Enter a valid date for {PropertyManagementValidationLabels.Label(field)}.";
        return new(
            message,
            "pm.validation.report.parameter.invalid_date",
            BuildContext(reportCode, field, message));
    }

    private static IReadOnlyDictionary<string, object?> BuildContext(string reportCode, string field, string message)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["reportCode"] = reportCode,
            ["field"] = field,
            ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = [message]
            }
        };
}
