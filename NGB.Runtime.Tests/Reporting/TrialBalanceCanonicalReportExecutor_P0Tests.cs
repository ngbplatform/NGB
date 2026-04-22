using System.Text.Json;
using FluentAssertions;
using NGB.Accounting.Reports.TrialBalance;
using NGB.Contracts.Reporting;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Canonical;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class TrialBalanceCanonicalReportExecutor_P0Tests
{
    [Fact]
    public async Task ExecuteAsync_Uses_Single_Account_Column_And_Maps_Group_Detail_Subtotal_Total_Rows()
    {
        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var propertyId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var reader = new StubTrialBalanceReportReader(
            new TrialBalanceReportPage(
                Rows:
                [
                    new TrialBalanceReportRow(TrialBalanceReportRowKind.Group, "Asset", 0m, 0m, 0m, 0m, 0, "group:Asset"),
                    new TrialBalanceReportRow(TrialBalanceReportRowKind.Detail, "1000 — Operating Cash", 0m, 10m, 5m, 5m, 1, "detail:1000", accountId),
                    new TrialBalanceReportRow(TrialBalanceReportRowKind.Subtotal, "Asset subtotal", 0m, 10m, 5m, 5m, 0, "subtotal:Asset")
                ],
                Total: 3,
                HasMore: false,
                Totals: new TrialBalanceReportTotals(0m, 10m, 5m, 5m)));

        var executor = new TrialBalanceCanonicalReportExecutor(reader);
        var definition = new ReportDefinitionDto(
            ReportCode: "accounting.trial_balance",
            Name: "Trial Balance",
            Group: "Accounting",
            Description: "Complete summary of ledger accounts",
            Mode: ReportExecutionMode.Canonical,
            Capabilities: new ReportCapabilitiesDto(
                AllowsFilters: true,
                AllowsRowGroups: false,
                AllowsColumnGroups: false,
                AllowsMeasures: false,
                AllowsDetailFields: false,
                AllowsSorting: false,
                AllowsShowDetails: false,
                AllowsSubtotals: true,
                AllowsGrandTotals: true),
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
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
                },
                Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["property_id"] = new(JsonSerializer.SerializeToElement(new[] { propertyId }), IncludeDescendants: true)
                },
                Offset: 999,
                Limit: 1),
            CancellationToken.None);

        var sheet = response.PrebuiltSheet;
        sheet.Should().NotBeNull();
        sheet!.Columns.Select(x => x.Code).Should().Equal("account", "debit_amount", "credit_amount");
        sheet.Rows.Select(x => x.RowKind).Should().Equal(ReportRowKind.Group, ReportRowKind.Detail, ReportRowKind.Subtotal, ReportRowKind.Total);
        sheet.Rows[0].Cells[0].Display.Should().Be("Asset");
        sheet.Rows[1].Cells[0].Display.Should().Be("1000 — Operating Cash");
        sheet.Rows[2].Cells[0].Display.Should().Be("Asset subtotal");
        sheet.Rows[3].Cells[0].Display.Should().Be("Total");

        var action = sheet.Rows[1].Cells[0].Action;
        action.Should().NotBeNull();
        action!.Kind.Should().Be(ReportCellActionKinds.OpenReport);
        action.Report.Should().NotBeNull();
        action.Report!.ReportCode.Should().Be("accounting.account_card");
        action.Report.Parameters.Should().Equal(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["from_utc"] = "2026-03-01",
            ["to_utc"] = "2026-03-31"
        });
        action.Report.Filters.Should().ContainKey("account_id");
        action.Report.Filters!["account_id"].Value.GetGuid().Should().Be(accountId);
        action.Report.Filters.Should().ContainKey("property_id");
        action.Report.Filters["property_id"].IncludeDescendants.Should().BeTrue();
        action.Report.Filters["property_id"].Value.EnumerateArray().Select(x => x.GetGuid()).Should().Equal(propertyId);

        response.Offset.Should().Be(0);
        response.Limit.Should().Be(sheet.Rows.Count);
        response.Total.Should().Be(sheet.Rows.Count);
        response.HasMore.Should().BeFalse();
        response.NextCursor.Should().BeNull();

        reader.LastRequest.Should().NotBeNull();
        reader.LastRequest!.ShowSubtotals.Should().BeTrue();
        reader.LastRequest.Offset.Should().Be(999);
        reader.LastRequest.Limit.Should().Be(1);
    }

    private sealed class StubTrialBalanceReportReader(TrialBalanceReportPage page) : ITrialBalanceReportReader
    {
        public TrialBalanceReportPageRequest? LastRequest { get; private set; }

        public Task<TrialBalanceReportPage> GetPageAsync(TrialBalanceReportPageRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(page);
        }
    }
}
