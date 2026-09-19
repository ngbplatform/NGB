using FluentAssertions;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.CashFlow;
using NGB.Accounting.Reports.CashFlowIndirect;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using NGB.Runtime.Reporting.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(AccountingPostgresCollection.Name)]
public sealed class CashFlowIndirect_Semantics_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Many_accounts_are_aggregated_into_statement_lines_and_unclassified_diagnostics_stay_bounded(bool unclassified)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync();
        await uow.Connection.ExecuteAsync("""
            INSERT INTO accounting_accounts(account_id,code,name,account_type,statement_section,negative_balance_policy,cash_flow_role,cash_flow_line_code)
              VALUES (md5('scale-cash')::uuid,'CASH','Cash',0,1,0,1,NULL);
            INSERT INTO accounting_accounts(account_id,code,name,account_type,statement_section,negative_balance_policy,cash_flow_role,cash_flow_line_code)
              SELECT md5('scale-wc-'||g)::uuid,'WC'||lpad(g::text,5,'0'),'Receivable '||g,0,1,0,@role,@line FROM generate_series(1,10001) g;
            INSERT INTO accounting_register_main(document_id,period,debit_account_id,credit_account_id,amount)
              SELECT md5('scale-cf-'||g)::uuid,'2035-01-15'::timestamptz,md5('scale-wc-'||g)::uuid,md5('scale-cash')::uuid,1 FROM generate_series(1,10001) g;
            """, new { role = unclassified ? (short)0 : (short)CashFlowRole.WorkingCapital,
                line = unclassified ? null : CashFlowSystemLineCodes.WorkingCapitalAccountsReceivable });
        using var probe = new NGB.Testing.Reporting.ReportPerformanceProbe("accounting.cash_flow_statement_indirect", "10001-accounts");
        var snapshot = await scope.ServiceProvider.GetRequiredService<ICashFlowIndirectSnapshotReader>().GetAsync(new(2035, 1, 1), new(2035, 1, 31));
        snapshot.BeginningCash.Should().Be(0);
        snapshot.EndingCash.Should().Be(-10001);
        probe.CommandCount.Should().BeLessThanOrEqualTo(8);
        if (unclassified)
        {
            snapshot.UnclassifiedCashRowCount.Should().Be(10001);
            snapshot.UnclassifiedCashRows.Should().HaveCount(50);
        }
        else
        {
            snapshot.UnclassifiedCashRows.Should().BeEmpty();
            snapshot.OperatingLines.Should().ContainSingle().Which.Amount.Should().Be(-10001);
        }
    }

    [Fact]
    public async Task Closing_profit_into_retained_earnings_does_not_erase_operating_cash_flow()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        await SeedCashFlowCoAAsync(sp);
        var management = sp.GetRequiredService<IChartOfAccountsManagementService>();
        await management.CreateAsync(new CreateAccountRequest(Code: "3200", Name: "Retained earnings", Type: AccountType.Equity, StatementSection: StatementSection.Equity));
        await PostAsync(sp, Guid.NewGuid(), Utc(new(2035, 1, 1)), "1000", "4000", 125m);
        var closingDocument = Guid.NewGuid();
        await PostAsync(sp, closingDocument, Utc(new(2035, 12, 31)), "4000", "3200", 125m);
        var uow = sp.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync();
        // Mark the synthetic posting as fiscal-year close, as PeriodClosingService does.
        await uow.Connection.ExecuteAsync("UPDATE accounting_posting_state SET operation=4 WHERE document_id=@closingDocument", new { closingDocument });
        var reader = sp.GetRequiredService<ICashFlowIndirectReportReader>();
        var report = await reader.GetAsync(new CashFlowIndirectReportRequest { FromInclusive = new(2035, 1, 1), ToInclusive = new(2035, 12, 31) });
        report.BeginningCash.Should().Be(0);
        report.EndingCash.Should().Be(125);
        report.Sections.Single(x => x.Section == CashFlowSection.Operating).Total.Should().Be(125);
        report.NetIncreaseDecreaseInCash.Should().Be(125);
    }

    [Fact]
    public async Task Report_Builds_Operating_Investing_And_Financing_CashFlows_From_Classified_Accounts()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var jan = new DateOnly(2035, 1, 1);
        var janUtc = Utc(jan);

        await SeedCashFlowCoAAsync(sp);

        await PostAsync(sp, Guid.CreateVersion7(), janUtc, debit: "1100", credit: "4000", amount: 100m);
        await PostAsync(sp, Guid.CreateVersion7(), janUtc, debit: "1000", credit: "1100", amount: 100m);
        await PostAsync(sp, Guid.CreateVersion7(), janUtc, debit: "5100", credit: "2000", amount: 40m);
        await PostAsync(sp, Guid.CreateVersion7(), janUtc, debit: "1500", credit: "1000", amount: 70m);
        await PostAsync(sp, Guid.CreateVersion7(), janUtc, debit: "1000", credit: "2500", amount: 50m);

        var reader = sp.GetRequiredService<ICashFlowIndirectReportReader>();

        var report = await reader.GetAsync(
            new CashFlowIndirectReportRequest
            {
                FromInclusive = jan,
                ToInclusive = jan
            },
            CancellationToken.None);

        report.Sections.Select(x => x.Section)
            .Should().Equal(CashFlowSection.Operating, CashFlowSection.Investing, CashFlowSection.Financing);

        report.Sections.Single(x => x.Section == CashFlowSection.Operating).Total.Should().Be(100m);
        report.Sections.Single(x => x.Section == CashFlowSection.Investing).Total.Should().Be(-70m);
        report.Sections.Single(x => x.Section == CashFlowSection.Financing).Total.Should().Be(50m);

        report.Sections.Single(x => x.Section == CashFlowSection.Operating).Lines
            .Select(x => x.Label)
            .Should().ContainInOrder("Net income", "Change in Accounts Payable");

        report.BeginningCash.Should().Be(0m);
        report.NetIncreaseDecreaseInCash.Should().Be(80m);
        report.EndingCash.Should().Be(80m);
    }

    [Fact]
    public async Task Report_WhenCashMovesAgainstUnclassifiedBalanceSheetCounterparty_FailsFast()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var jan = new DateOnly(2036, 1, 1);
        var janUtc = Utc(jan);

        var mgmt = sp.GetRequiredService<IChartOfAccountsManagementService>();

        await mgmt.CreateAsync(new CreateAccountRequest(
            Code: "1000",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            CashFlowRole: CashFlowRole.CashEquivalent));

        await mgmt.CreateAsync(new CreateAccountRequest(
            Code: "1500",
            Name: "Equipment",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets));

        await PostAsync(sp, Guid.CreateVersion7(), janUtc, debit: "1500", credit: "1000", amount: 25m);

        var reader = sp.GetRequiredService<ICashFlowIndirectReportReader>();

        var act = () => reader.GetAsync(
            new CashFlowIndirectReportRequest
            {
                FromInclusive = jan,
                ToInclusive = jan
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<AccountingReportValidationException>()
            .WithMessage("*unclassified balance-sheet counterparties*1500 Equipment*");
    }

    private static async Task SeedCashFlowCoAAsync(IServiceProvider sp)
    {
        var mgmt = sp.GetRequiredService<IChartOfAccountsManagementService>();

        await mgmt.CreateAsync(new CreateAccountRequest(
            Code: "1000",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            CashFlowRole: CashFlowRole.CashEquivalent));

        await mgmt.CreateAsync(new CreateAccountRequest(
            Code: "1100",
            Name: "Accounts Receivable",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            CashFlowRole: CashFlowRole.WorkingCapital,
            CashFlowLineCode: CashFlowSystemLineCodes.WorkingCapitalAccountsReceivable));

        await mgmt.CreateAsync(new CreateAccountRequest(
            Code: "2000",
            Name: "Accounts Payable",
            Type: AccountType.Liability,
            StatementSection: StatementSection.Liabilities,
            CashFlowRole: CashFlowRole.WorkingCapital,
            CashFlowLineCode: CashFlowSystemLineCodes.WorkingCapitalAccountsPayable));

        await mgmt.CreateAsync(new CreateAccountRequest(
            Code: "1500",
            Name: "Property and Equipment",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            CashFlowRole: CashFlowRole.InvestingCounterparty,
            CashFlowLineCode: CashFlowSystemLineCodes.InvestingPropertyEquipmentNet));

        await mgmt.CreateAsync(new CreateAccountRequest(
            Code: "2500",
            Name: "Bank Debt",
            Type: AccountType.Liability,
            StatementSection: StatementSection.Liabilities,
            CashFlowRole: CashFlowRole.FinancingCounterparty,
            CashFlowLineCode: CashFlowSystemLineCodes.FinancingDebtNet));

        await mgmt.CreateAsync(new CreateAccountRequest(
            Code: "4000",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income));

        await mgmt.CreateAsync(new CreateAccountRequest(
            Code: "5100",
            Name: "Expense",
            Type: AccountType.Expense,
            StatementSection: StatementSection.Expenses));
    }

    private static DateTime Utc(DateOnly periodMonthStart) =>
        new(periodMonthStart.Year, periodMonthStart.Month, periodMonthStart.Day, 0, 0, 0, DateTimeKind.Utc);

    private static async Task PostAsync(
        IServiceProvider sp,
        Guid documentId,
        DateTime periodUtc,
        string debit,
        string credit,
        decimal amount)
    {
        var engine = sp.GetRequiredService<PostingEngine>();

        await engine.PostAsync(
            operation: NGB.Accounting.PostingState.PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var coa = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, periodUtc, coa.Get(debit), coa.Get(credit), amount);
            },
            manageTransaction: true);
    }
}
