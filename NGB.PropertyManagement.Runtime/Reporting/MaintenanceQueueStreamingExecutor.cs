using System.Runtime.CompilerServices;
using System.Text.Json;
using NGB.Contracts.Reporting;
using NGB.PropertyManagement.Reporting;
using NGB.Runtime.Reporting.Streaming;

namespace NGB.PropertyManagement.Runtime.Reporting;

public sealed class MaintenanceQueueStreamingExecutor(
    IMaintenanceQueueStreamReader reader,
    TimeProvider timeProvider)
    : IStreamingReportExecutor
{
    public string ReportCode => PropertyManagementCodes.MaintenanceQueue;
    private sealed record Input(ReportDefinitionDto Definition, MaintenanceQueueQuery Query);

    public string Prepare(ReportDefinitionDto definition, ReportExecutionRequestDto request)
    {
        var parameters = new Dictionary<string, string>(request.Parameters ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);

        if (!parameters.ContainsKey("as_of_utc"))
            parameters["as_of_utc"] = timeProvider.GetUtcNow().ToString("yyyy-MM-dd");

        return JsonSerializer.Serialize(new Input(definition, MaintenanceQueueCanonicalReportExecutor.CreateQuery(definition, request with { Parameters = parameters })));
    }

    public ReportSheetDto Template(string preparedJson)
    {
        var input = JsonSerializer.Deserialize<Input>(preparedJson)!;
        return MaintenanceQueueCanonicalReportExecutor.CreateSheet(input.Definition, input.Query.AsOfUtc, []);
    }

    public async IAsyncEnumerable<ReportRowWrite> ReadAsync(string preparedJson, [EnumeratorCancellation] CancellationToken ct)
    {
        var input = JsonSerializer.Deserialize<Input>(preparedJson)!;
        var ordinal = 0;

        await foreach (var batch in reader.ReadAsync(input.Query, ct))
        {
            foreach (var row in MaintenanceQueueCanonicalReportExecutor.CreateSheet(input.Definition, input.Query.AsOfUtc, batch).Rows)
            {
                yield return new(ordinal++, row);
            }
        }
    }
}
