using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Tools.Exceptions;
using NGB.Tools.Normalization;

namespace NGB.Runtime.Reporting;

/// <summary>
/// Shared execution router for the final reporting stack.
///
/// Resolution order:
/// 1. specialized executor by report code (canonical/bespoke/prebuilt reports);
/// 2. generic tabular executor for composable dataset-backed reports.
/// </summary>
public sealed class CompositeReportPlanExecutor : IReportPlanExecutor
{
    private readonly ITabularReportPlanExecutor? _tabularExecutor;
    private readonly IReadOnlyDictionary<string, IReportSpecializedPlanExecutor> _specializedExecutors;

    public CompositeReportPlanExecutor(
        ITabularReportPlanExecutor? tabularExecutor,
        IEnumerable<IReportSpecializedPlanExecutor> specializedExecutors)
    {
        _tabularExecutor = tabularExecutor;

        var dict = new Dictionary<string, IReportSpecializedPlanExecutor>(StringComparer.OrdinalIgnoreCase);
        foreach (var executor in specializedExecutors)
        {
            var reportCodeNorm = CodeNormalizer.NormalizeCodeNorm(executor.ReportCode, nameof(executor.ReportCode));
            if (!dict.TryAdd(reportCodeNorm, executor))
                throw new NgbConfigurationViolationException($"Reporting specialized executor '{reportCodeNorm}' is registered more than once.");
        }

        _specializedExecutors = dict;
    }

    public Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        string reportCode,
        string? datasetCode,
        IReadOnlyList<ReportPlanGrouping> rowGroups,
        IReadOnlyList<ReportPlanGrouping> columnGroups,
        IReadOnlyList<ReportPlanFieldSelection> detailFields,
        IReadOnlyList<ReportPlanMeasure> measures,
        IReadOnlyList<ReportPlanSort> sorts,
        IReadOnlyList<ReportPlanPredicate> predicates,
        IReadOnlyList<ReportPlanParameter> parameters,
        ReportPlanPaging paging,
        CancellationToken ct)
    {
        if (definition is null)
            throw new NgbArgumentRequiredException(nameof(definition));

        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        var reportCodeNorm = CodeNormalizer.NormalizeCodeNorm(reportCode, nameof(reportCode));
        if (_specializedExecutors.TryGetValue(reportCodeNorm, out var specialized))
            return specialized.ExecuteAsync(definition, request, ct);

        if (_tabularExecutor is null)
            throw new NgbConfigurationViolationException($"Reporting report '{reportCodeNorm}' requires a tabular plan executor registration.");

        return _tabularExecutor.ExecuteAsync(
            definition,
            request,
            reportCodeNorm,
            datasetCode,
            rowGroups,
            columnGroups,
            detailFields,
            measures,
            sorts,
            predicates,
            parameters,
            paging,
            ct);
    }
}
