using NGB.Tools.Exceptions;

namespace NGB.Core.Reporting.Exceptions;

public sealed class ReportVariantNotFoundException(string reportCode, string variantCode) : NgbNotFoundException(
    message: $"Unknown report variant '{variantCode}' for report '{reportCode}'.",
    errorCode: Code,
    context: new Dictionary<string, object?>
    {
        ["reportCode"] = reportCode,
        ["variantCode"] = variantCode
    })
{
    public const string Code = "report.variant.not_found";
}
