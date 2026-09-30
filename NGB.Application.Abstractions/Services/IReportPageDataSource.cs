using System.Text.Json;
using NGB.Contracts.Reporting;

namespace NGB.Application.Abstractions.Services;

/// <summary>Provider-neutral, bounded reads at the requested aggregation grain.</summary>
public interface IReportPageDataSource
{
    Task<ReportDataPage> ReadPageAsync(
        ReportDataQuery query,
        ReportPlanPaging paging,
        ReportRowSelection? selection,
        CancellationToken ct);
}

/// <summary>A bounded union of row keys. Providers apply it as one set-based query, never one query per key.</summary>
public sealed record ReportRowSelection(
    IReadOnlyList<ReportSelectionField> Fields,
    IReadOnlyList<IReadOnlyList<JsonElement>> Keys);

public sealed record ReportSelectionField(string FieldCode, ReportTimeGrain? TimeGrain = null);
