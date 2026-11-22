using System.Globalization;
using EquipmentInspection.Definitions;
using EquipmentInspection.Runtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Runtime.Security;

namespace EquipmentInspection.Api;

[Authorize]
[ApiController]
[Route("api/inspection-dashboard")]
public sealed class InspectionDashboardController(IAuthorizedReportReadService reports, INgbAccessChecker access, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<Snapshot> Get([FromQuery] DateOnly? asOf, CancellationToken ct)
    {
        await access.RequireAsync("page", EquipmentMainMenuContributor.DashboardCode, "view", ct);
        // The source report defines its day boundaries in UTC.
        var day = asOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var parameters = new Dictionary<string, string>
        {
            ["from_utc"] = new DateOnly(day.Year, day.Month, 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["to_utc"] = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };
        var request = new ReportExecutionRequestDto(Parameters: parameters);
        long total = 0;
        long followUp = 0;
        long rejected = 0;
        long passed = 0;
        while (true)
        {
            var page = await reports.ExecuteAsync(InspectionReports.Summary, request, ct);
            foreach (var row in page.Sheet.Rows.Where(row => row.RowKind == ReportRowKind.Group))
            {
                var count = row.Cells[^1].Value!.Value.GetInt64();
                total += count;
                switch (row.Cells[0].Display)
                {
                    case "Follow-up":
                        followUp += count;
                        break;
                    case "Rejected":
                        rejected += count;
                        break;
                    case "Passed":
                        passed += count;
                        break;
                }
            }

            if (!page.HasMore)
                return new Snapshot(day, parameters, total, followUp, rejected, passed);

            if (string.IsNullOrEmpty(page.NextCursor) || page.NextCursor == request.Cursor)
                throw new InvalidOperationException("The inspection summary did not advance.");
            request = request with { Cursor = page.NextCursor };
        }
    }

    public sealed record Snapshot(
        DateOnly AsOf,
        IReadOnlyDictionary<string, string> Parameters,
        long Total,
        long FollowUp,
        long Rejected,
        long Passed);
}
