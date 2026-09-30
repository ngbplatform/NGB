using NGB.Contracts.Reporting;

namespace NGB.Runtime.Reporting.Streaming;

/// <summary>Preparation runs with the requesting user's variant/filter context. Execution uses the frozen input.</summary>
public interface IStreamingReportExecutor
{
    string ReportCode { get; }
    string Prepare(ReportDefinitionDto definition, ReportExecutionRequestDto request);
    ReportSheetDto Template(string preparedJson);
    IAsyncEnumerable<ReportRowWrite> ReadAsync(string preparedJson, CancellationToken ct);
}

/// <summary>A row may be replaced while its attempt is Running (for example an inline group total).
/// Only the final, complete sequence becomes visible to readers.</summary>
public sealed record ReportRowWrite(int Ordinal, ReportSheetRowDto Row);
