using System.Runtime.CompilerServices;
using System.Text.Json;
using NGB.Contracts.Reporting;
using NGB.PropertyManagement.Definitions;
using NGB.PropertyManagement.Reporting;
using NGB.Runtime.Reporting.Canonical;
using NGB.Runtime.Reporting.Streaming;

namespace NGB.PropertyManagement.Runtime.Reporting;

public sealed class OccupancySummaryStreamingExecutor(
    IOccupancySummaryReportReader reader,
    TimeProvider timeProvider)
    : IStreamingReportExecutor
{
    public string ReportCode => PropertyManagementSecurityDefaults.OccupancySummaryReport;

    private sealed record Input(ReportDefinitionDto Definition, Guid? BuildingId, DateOnly AsOf, bool Totals);

    public string Prepare(ReportDefinitionDto definition, ReportExecutionRequestDto request)
        => JsonSerializer.Serialize(new Input(
            definition,
            CanonicalReportExecutionHelper.GetOptionalGuidFilter(definition, request, "building_id"),
            CanonicalReportExecutionHelper.GetOptionalDateOnlyParameter(definition, request, "as_of_utc")
                ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime),
            request.Layout?.ShowGrandTotals != false));

    public ReportSheetDto Template(string preparedJson)
    {
        var input = JsonSerializer.Deserialize<Input>(preparedJson)!;
        return OccupancySummaryCanonicalReportExecutor.CreateSheet(input.Definition, input.BuildingId, input.AsOf, [], null);
    }

    public async IAsyncEnumerable<ReportRowWrite> ReadAsync(
        string preparedJson,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var input = JsonSerializer.Deserialize<Input>(preparedJson)!;
        var ordinal = 0;
        var count = 0;
        var units = 0;
        var occupied = 0;

        await foreach (var batch in reader.ReadAsync(input.BuildingId, input.AsOf, ct))
        {
            checked
            {
                count += batch.Count;
                units += batch.Sum(x => x.TotalUnits);
                occupied += batch.Sum(x => x.OccupiedUnits);
            }

            var sheet = OccupancySummaryCanonicalReportExecutor.CreateSheet(input.Definition, input.BuildingId, input.AsOf, batch, null);

            foreach (var row in sheet.Rows)
            {
                yield return new(ordinal++, row);
            }
        }

        if (input.Totals && count > 0)
            yield return new(ordinal, OccupancySummaryCanonicalReportExecutor.ToTotalRow(new(input.AsOf, count, units, occupied)));
    }
}
