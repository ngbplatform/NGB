using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.CashFlow;
using NGB.Accounting.Reports.CashFlowIndirect;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using NGB.Runtime.Reporting.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class CashFlowIndirect_Semantics_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Report_Builds_Operating_Investing_And_Financing_CashFlows_From_Classified_Accounts()
    {
        await Fixture.ResetDatabaseAsync();
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
        await Fixture.ResetDatabaseAsync();
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
