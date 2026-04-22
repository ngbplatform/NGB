using System.Text.Json;
using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.IncomeStatement;
using NGB.Contracts.Reporting;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Canonical;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class IncomeStatementCanonicalReportExecutor_P0Tests
{
    [Fact]
    public async Task ExecuteAsync_Uses_TrialBalanceStyle_Account_Column_And_AccountCard_Drilldown()
    {
        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var propertyId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var reader = new StubIncomeStatementReportReader(
            new IncomeStatementReport
            {
                FromInclusive = new DateOnly(2026, 3, 1),
                ToInclusive = new DateOnly(2026, 3, 31),
                Sections =
                [
                    new IncomeStatementSection
                    {
                        Section = StatementSection.Income,
                        Lines =
                        [
                            new IncomeStatementLine
                            {
                                AccountId = accountId,
                                AccountCode = "4000",
                                AccountName = "Rental Income",
                                Amount = 25m
                            }
                        ],
                        Total = 25m
                    }
                ],
                TotalIncome = 25m,
                TotalExpenses = 0m,
                TotalOtherIncome = 0m,
                TotalOtherExpense = 0m,
                NetIncome = 25m
            });

        var executor = new IncomeStatementCanonicalReportExecutor(reader);
        var definition = new ReportDefinitionDto(
            ReportCode: "accounting.income_statement",
            Name: "Income Statement",
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
                Limit: 50),
            CancellationToken.None);

        var sheet = response.PrebuiltSheet;
        sheet.Should().NotBeNull();
        sheet!.Columns.Select(x => x.Code).Should().Equal("account", "amount");
        sheet.Meta!.HasRowOutline.Should().BeTrue();
        sheet.Rows.Select(x => x.RowKind).Should().ContainInOrder(ReportRowKind.Group, ReportRowKind.Detail, ReportRowKind.Subtotal);
        sheet.Rows.Should().Contain(x => x.RowKind == ReportRowKind.Total && x.Cells[0].Display == "Net Income");
        sheet.Rows[0].Cells[0].Display.Should().Be("Income");
        sheet.Rows[0].Cells[1].Display.Should().BeEmpty();
        sheet.Rows[1].Cells[0].Display.Should().Be("4000 — Rental Income");
        sheet.Rows[1].OutlineLevel.Should().Be(1);
        sheet.Rows[2].Cells[0].Display.Should().Be("Income total");

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

    [Fact]
    public async Task ExecuteAsync_Humanizes_CostOfGoodsSold_Section()
    {
        var reader = new StubIncomeStatementReportReader(
            new IncomeStatementReport
            {
                FromInclusive = new DateOnly(2026, 3, 1),
                ToInclusive = new DateOnly(2026, 3, 31),
                Sections =
                [
                    new IncomeStatementSection
                    {
                        Section = StatementSection.CostOfGoodsSold,
                        Lines =
                        [
                            new IncomeStatementLine
                            {
                                AccountId = Guid.NewGuid(),
                                AccountCode = "5000",
                                AccountName = "COGS",
                                Amount = 12m
                            }
                        ],
                        Total = 12m
                    }
                ],
                TotalIncome = 0m,
                TotalExpenses = 12m,
                TotalOtherIncome = 0m,
                TotalOtherExpense = 0m,
                NetIncome = -12m
            });

        var executor = new IncomeStatementCanonicalReportExecutor(reader);
        var definition = new ReportDefinitionDto(
            ReportCode: "accounting.income_statement",
            Name: "Income Statement",
            Parameters:
            [
                new ReportParameterMetadataDto("from_utc", "date", true, Label: "From"),
                new ReportParameterMetadataDto("to_utc", "date", true, Label: "To")
            ]);

        var response = await executor.ExecuteAsync(
            definition,
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
                }),
            CancellationToken.None);

        response.PrebuiltSheet!.Rows[0].Cells[0].Display.Should().Be("Cost of Goods Sold");
    }

    private sealed class StubIncomeStatementReportReader(IncomeStatementReport report) : IIncomeStatementReportReader
    {
        public Task<IncomeStatementReport> GetAsync(IncomeStatementReportRequest request, CancellationToken ct = default)
            => Task.FromResult(report);
    }
}
