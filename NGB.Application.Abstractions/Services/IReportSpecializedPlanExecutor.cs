using NGB.Contracts.Reporting;

namespace NGB.Application.Abstractions.Services;

/// <summary>
/// Shared execution hook for report definitions that bypass the generic tabular
/// dataset executor but still run through the unified reporting pipeline:
/// ReportEngine -> Planner -> IReportPlanExecutor -> ReportSheetBuilder.
///
/// Typical use cases:
/// - platform canonical accounting reports backed by bespoke readers;
/// - vertical reports whose rendering is naturally prebuilt rather than tabular.
/// </summary>
public interface IReportSpecializedPlanExecutor
{
    string ReportCode { get; }

    Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct);
}
