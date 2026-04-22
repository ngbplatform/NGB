using NGB.Tools.Exceptions;

namespace NGB.Core.Reporting.Exceptions;

public sealed class ReportTypeNotFoundException(string reportCode) : NgbNotFoundException(
    message: $"Unknown report code '{reportCode}'.",
    errorCode: Code,
    context: new Dictionary<string, object?>
    {
        ["reportCode"] = reportCode
    })
{
    public const string Code = "report.type.not_found";

    public string ReportCode { get; } = reportCode;
}
