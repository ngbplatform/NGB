using FluentAssertions;
using NGB.Accounting.CashFlow;
using NGB.Accounting.Reports.CashFlowIndirect;
using NGB.Contracts.Reporting;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Canonical;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class CashFlowIndirectCanonicalReportExecutor_P0Tests
{
    [Fact]
    public async Task ExecuteAsync_Renders_Bounded_Indirect_CashFlow_Sheet_With_Reconciliation()
    {
        var reader = new StubReportReader(
            new CashFlowIndirectReport
            {
                FromInclusive = new DateOnly(2026, 1, 1),
                ToInclusive = new DateOnly(2026, 3, 1),
                Sections =
                [
                    new CashFlowIndirectSectionModel
                    {
                        Section = CashFlowSection.Operating,
                        Label = "Operating Activities",
                        Lines =
                        [
                            new CashFlowIndirectLine { LineCode = "NET_INCOME", Label = "Net income", Amount = 60m, IsSynthetic = true },
                            new CashFlowIndirectLine { LineCode = "op_wc_accounts_receivable", Label = "Change in Accounts Receivable", Amount = 40m, IsSynthetic = false }
                        ],
                        Total = 100m
                    },
                    new CashFlowIndirectSectionModel
                    {
                        Section = CashFlowSection.Investing,
                        Label = "Investing Activities",
                        Lines =
                        [
                            new CashFlowIndirectLine { LineCode = "inv_property_equipment_net", Label = "Property and equipment, net", Amount = -70m, IsSynthetic = false }
                        ],
                        Total = -70m
                    },
                    new CashFlowIndirectSectionModel
                    {
                        Section = CashFlowSection.Financing,
                        Label = "Financing Activities",
                        Lines =
                        [
                            new CashFlowIndirectLine { LineCode = "fin_debt_net", Label = "Borrowings and repayments, net", Amount = 50m, IsSynthetic = false }
                        ],
                        Total = 50m
                    }
                ],
                BeginningCash = 10m,
                NetIncreaseDecreaseInCash = 80m,
                EndingCash = 90m
            });

        var executor = new CashFlowIndirectCanonicalReportExecutor(reader);
        var definition = new ReportDefinitionDto(
            ReportCode: "accounting.cash_flow_statement_indirect",
            Name: "Cash Flow Statement (Indirect)",
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
                    ["from_utc"] = "2026-01-01",
                    ["to_utc"] = "2026-03-31"
                }),
            CancellationToken.None);

        response.HasMore.Should().BeFalse();
        response.NextCursor.Should().BeNull();
        response.PrebuiltSheet.Should().NotBeNull();

        var sheet = response.PrebuiltSheet!;
        sheet.Columns.Select(x => x.Code).Should().Equal("line", "amount");
        sheet.Rows.Select(x => x.Cells[0].Display).Should().ContainInOrder(
            "Operating Activities",
            "Net income",
            "Change in Accounts Receivable",
            "Net cash from operating activities",
            "Investing Activities",
            "Property and equipment, net",
            "Net cash from investing activities",
            "Financing Activities",
            "Borrowings and repayments, net",
            "Net cash from financing activities",
            "Reconciliation",
            "Cash and cash equivalents at beginning of period",
            "Net increase (decrease) in cash and cash equivalents",
            "Cash and cash equivalents at end of period");
    }

    private sealed class StubReportReader(CashFlowIndirectReport report) : ICashFlowIndirectReportReader
    {
        public Task<CashFlowIndirectReport> GetAsync(
            CashFlowIndirectReportRequest request,
            CancellationToken ct = default)
            => Task.FromResult(report);
    }
}
