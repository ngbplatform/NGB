using NGB.Tools.Exceptions;

namespace NGB.Core.Reporting.Exceptions;

public sealed class ReportDatasetTypeNotFoundException(string datasetCode) : NgbNotFoundException(
    message: $"Unknown report dataset code '{datasetCode}'.",
    errorCode: Code,
    context: new Dictionary<string, object?>
    {
        ["datasetCode"] = datasetCode
    })
{
    public const string Code = "report.dataset.not_found";

    public string DatasetCode { get; } = datasetCode;
}
