using NGB.Application.Abstractions.Services;
using NGB.Contracts.Admin;
using NGB.Core.Reporting;
using NGB.Core.Security;

namespace NGB.Runtime.Admin;

/// <summary>
/// Platform-level accounting report navigation that can be reused by any vertical host.
/// </summary>
public sealed class AccountingReportsMainMenuContributor : IMainMenuContributor
{
    public Task<IReadOnlyList<MainMenuGroupDto>> ContributeAsync(CancellationToken ct)
    {
        var group = new MainMenuGroupDto(
            Label: "Accounting",
            Items:
            [
                new MainMenuItemDto(
                    Kind: NgbResourceKinds.Page,
                    Code: AccountingReportCodes.TrialBalance,
                    Label: "Trial Balance",
                    Route: "/reports/accounting.trial_balance",
                    Icon: "bar-chart",
                    Ordinal: 10),
                new MainMenuItemDto(
                    Kind: NgbResourceKinds.Page,
                    Code: AccountingReportCodes.BalanceSheet,
                    Label: "Balance Sheet",
                    Route: "/reports/accounting.balance_sheet",
                    Icon: "bar-chart",
                    Ordinal: 20),
                new MainMenuItemDto(
                    Kind: NgbResourceKinds.Page,
                    Code: AccountingReportCodes.IncomeStatement,
                    Label: "Income Statement",
                    Route: "/reports/accounting.income_statement",
                    Icon: "bar-chart",
                    Ordinal: 30),
                new MainMenuItemDto(
                    Kind: NgbResourceKinds.Page,
                    Code: AccountingReportCodes.CashFlowStatementIndirect,
                    Label: "Cash Flow Statement",
                    Route: "/reports/accounting.cash_flow_statement_indirect",
                    Icon: "bar-chart",
                    Ordinal: 50),
                new MainMenuItemDto(
                    Kind: NgbResourceKinds.Page,
                    Code: AccountingReportCodes.StatementOfChangesInEquity,
                    Label: "Statement of Changes in Equity",
                    Route: "/reports/accounting.statement_of_changes_in_equity",
                    Icon: "bar-chart",
                    Ordinal: 40),
                new MainMenuItemDto(
                    Kind: NgbResourceKinds.Page,
                    Code: AccountingReportCodes.GeneralJournal,
                    Label: "General Journal",
                    Route: "/reports/accounting.general_journal",
                    Icon: "receipt",
                    Ordinal: 60),
                new MainMenuItemDto(
                    Kind: NgbResourceKinds.Page,
                    Code: AccountingReportCodes.AccountCard,
                    Label: "Account Card",
                    Route: "/reports/accounting.account_card",
                    Icon: "book-open",
                    Ordinal: 70),
                new MainMenuItemDto(
                    Kind: NgbResourceKinds.Page,
                    Code: AccountingReportCodes.GeneralLedgerAggregated,
                    Label: "General Ledger",
                    Route: "/reports/accounting.general_ledger_aggregated",
                    Icon: "book-open",
                    Ordinal: 80),
                new MainMenuItemDto(
                    Kind: NgbResourceKinds.Page,
                    Code: AccountingReportCodes.LedgerAnalysis,
                    Label: "Ledger Analysis",
                    Route: "/reports/accounting.ledger.analysis",
                    Icon: "bar-chart",
                    Ordinal: 90)
            ],
            Ordinal: 60,
            Icon: "calculator");

        return Task.FromResult<IReadOnlyList<MainMenuGroupDto>>([group]);
    }
}
