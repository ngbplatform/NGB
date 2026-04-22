using NGB.Tools.Exceptions;

namespace NGB.Core.Reporting.Exceptions;

public sealed class ReportVariantCodeConflictException(string reportCode, string variantCode) : NgbConflictException(
    message: $"Report variant code '{variantCode}' already exists for report '{reportCode}'.",
    errorCode: Code,
    context: new Dictionary<string, object?>
    {
        ["reportCode"] = reportCode,
        ["variantCode"] = variantCode
    })
{
    public const string Code = "report.variant.code_conflict";
}
