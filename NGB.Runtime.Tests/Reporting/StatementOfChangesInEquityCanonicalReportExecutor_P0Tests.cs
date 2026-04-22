using System.Text.Json;
using FluentAssertions;
using NGB.Accounting.Reports.StatementOfChangesInEquity;
using NGB.Contracts.Reporting;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Canonical;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class StatementOfChangesInEquityCanonicalReportExecutor_P0Tests
{
    [Fact]
    public async Task ExecuteAsync_Renders_Real_Components_With_AccountCard_Drilldown_And_Leaves_Synthetic_Line_NonClickable()
    {
        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var propertyId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var reader = new StubReportReader(
            new StatementOfChangesInEquityReport
            {
                FromInclusive = new DateOnly(2026, 1, 1),
                ToInclusive = new DateOnly(2026, 3, 31),
                Lines =
                [
                    new StatementOfChangesInEquityLine
                    {
                        AccountId = accountId,
                        ComponentCode = "84",
                        ComponentName = "Retained Earnings",
                        IsSynthetic = false,
                        OpeningAmount = 10m,
                        ChangeAmount = 30m,
                        ClosingAmount = 40m
                    },
                    new StatementOfChangesInEquityLine
                    {
                        AccountId = Guid.Empty,
                        ComponentCode = "CURR_EARNINGS",
                        ComponentName = "Current Earnings (Unclosed)",
                        IsSynthetic = true,
                        OpeningAmount = 0m,
                        ChangeAmount = 15m,
                        ClosingAmount = 15m
                    }
                ],
                TotalOpening = 10m,
                TotalChange = 45m,
                TotalClosing = 55m
            });

        var executor = new StatementOfChangesInEquityCanonicalReportExecutor(reader);
        var definition = new ReportDefinitionDto(
            ReportCode: "accounting.statement_of_changes_in_equity",
            Name: "Statement of Changes in Equity",
            Parameters:
            [
                new ReportParameterMetadataDto("from_utc", "date", true, Label: "From"),
                new ReportParameterMetadataDto("to_utc", "date", true, Label: "To")
            ],
            Filters:
            [
                new ReportFilterFieldDto("property_id", "Property", "guid", IsMulti: true, SupportsIncludeDescendants: true)
            ]);

        var response = await executor.ExecuteAsync(
            definition,
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-01-01",
                    ["to_utc"] = "2026-03-31"
                },
                Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["property_id"] = new(JsonSerializer.SerializeToElement(new[] { propertyId }), IncludeDescendants: true)
                }),
            CancellationToken.None);

        var sheet = response.PrebuiltSheet;
        sheet.Should().NotBeNull();
        sheet!.Columns.Select(x => x.Code).Should().Equal("component", "opening", "change", "closing");
        sheet.Meta!.HasRowOutline.Should().BeFalse();
        sheet.Rows.Should().HaveCount(3);

        sheet.Rows[0].Cells[0].Display.Should().Be("84 — Retained Earnings");
        sheet.Rows[1].Cells[0].Display.Should().Be("Current Earnings (Unclosed)");
        sheet.Rows[2].Cells[0].Display.Should().Be("Total Equity");

        var realAction = sheet.Rows[0].Cells[0].Action;
        realAction.Should().NotBeNull();
        realAction!.Report!.ReportCode.Should().Be("accounting.account_card");
        realAction.Report.Parameters.Should().Equal(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["from_utc"] = "2026-01-01",
            ["to_utc"] = "2026-03-31"
        });
        realAction.Report.Filters.Should().ContainKeys("account_id", "property_id");

        sheet.Rows[1].Cells[0].Action.Should().BeNull();
    }

    private sealed class StubReportReader(StatementOfChangesInEquityReport report) : IStatementOfChangesInEquityReportReader
    {
        public Task<StatementOfChangesInEquityReport> GetAsync(
            StatementOfChangesInEquityReportRequest request,
            CancellationToken ct = default)
            => Task.FromResult(report);
    }
}
