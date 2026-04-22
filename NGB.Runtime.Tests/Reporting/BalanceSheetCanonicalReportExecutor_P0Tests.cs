using System.Text.Json;
using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.BalanceSheet;
using NGB.Contracts.Reporting;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Canonical;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class BalanceSheetCanonicalReportExecutor_P0Tests
{
    [Fact]
    public async Task ExecuteAsync_Uses_TrialBalanceStyle_Account_Column_And_AccountCard_Drilldown()
    {
        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var propertyId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var reader = new StubBalanceSheetReportReader(
            new BalanceSheetReport
            {
                AsOfPeriod = new DateOnly(2026, 3, 31),
                Sections =
                [
                    new BalanceSheetSection
                    {
                        Section = StatementSection.Assets,
                        Title = "Assets",
                        Lines =
                        [
                            new BalanceSheetLine
                            {
                                AccountId = accountId,
                                AccountCode = "1000",
                                AccountName = "Operating Cash",
                                Amount = 25m
                            }
                        ],
                        Total = 25m
                    }
                ],
                TotalAssets = 25m,
                TotalLiabilities = 0m,
                TotalEquity = 25m,
                TotalLiabilitiesAndEquity = 25m,
                Difference = 0m,
                IsBalanced = true
            });

        var executor = new BalanceSheetCanonicalReportExecutor(reader);
        var definition = new ReportDefinitionDto(
            ReportCode: "accounting.balance_sheet",
            Name: "Balance Sheet",
            Parameters:
            [
                new ReportParameterMetadataDto("as_of_utc", "date", true, Label: "As Of")
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
                    ["as_of_utc"] = "2026-03-31"
                },
                Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["property_id"] = new(JsonSerializer.SerializeToElement(new[] { propertyId }), IncludeDescendants: true)
                },
                Limit: 50),
            CancellationToken.None);

        var sheet = response.PrebuiltSheet;
        sheet.Should().NotBeNull();
        sheet!.Columns.Select(x => x.Code).Should().Equal("account", "amount");
        sheet.Meta!.HasRowOutline.Should().BeTrue();
        sheet.Rows.Select(x => x.RowKind).Should().ContainInOrder(ReportRowKind.Group, ReportRowKind.Detail, ReportRowKind.Subtotal);
        sheet.Rows.Should().Contain(x => x.RowKind == ReportRowKind.Total);
        sheet.Rows[0].Cells[0].Display.Should().Be("Assets");
        sheet.Rows[0].Cells[1].Display.Should().BeEmpty();
        sheet.Rows[1].Cells[0].Display.Should().Be("1000 — Operating Cash");
        sheet.Rows[1].OutlineLevel.Should().Be(1);
        sheet.Rows[2].Cells[0].Display.Should().Be("Assets total");

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
        action.Report.Filters.Should().ContainKeys("account_id", "property_id");
        action.Report.Filters!["account_id"].Value.GetGuid().Should().Be(accountId);
        action.Report.Filters["property_id"].Value.EnumerateArray().Select(x => x.GetGuid()).Should().Equal(propertyId);
        action.Report.Filters["property_id"].IncludeDescendants.Should().BeTrue();
    }

    private sealed class StubBalanceSheetReportReader(BalanceSheetReport report) : IBalanceSheetReportReader
    {
        public Task<BalanceSheetReport> GetAsync(BalanceSheetReportRequest request, CancellationToken ct = default)
            => Task.FromResult(report);
    }
}
