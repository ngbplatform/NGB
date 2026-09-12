namespace NGB.Application.Abstractions.Services;

public sealed record ReportDataQuery(
    string ReportCode,
    string? DatasetCode,
    IReadOnlyList<ReportPlanGrouping> RowGroups,
    IReadOnlyList<ReportPlanGrouping> ColumnGroups,
    IReadOnlyList<ReportPlanFieldSelection> DetailFields,
    IReadOnlyList<ReportPlanMeasure> Measures,
    IReadOnlyList<ReportPlanSort> Sorts,
    IReadOnlyList<ReportPlanPredicate> Predicates,
    IReadOnlyList<ReportPlanParameter> Parameters);

/// <summary>Reads the entire selected dataset in bounded batches within the current report read session.</summary>
public interface IStreamingReportDataSource
{
    IAsyncEnumerable<ReportDataPage> ReadAsync(ReportDataQuery query, CancellationToken ct);
}
