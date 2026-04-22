using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.AccountCard;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using NGB.Runtime.Periods;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class Reports_PeriodBoundarySemanticsTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string Cash = "50";
    private const string Revenue = "90.1";

    [Fact]
    public async Task GeneralJournal_respects_day_boundaries_inclusive()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        var d1 = Guid.CreateVersion7();
        var d2 = Guid.CreateVersion7();

        var jan31 = new DateTime(2026, 1, 31, 23, 59, 0, DateTimeKind.Utc);
        var feb01 = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        await PostCashSaleAsync(host, d1, jan31, 100m);
        await PostCashSaleAsync(host, d2, feb01, 200m);

        // Act + Assert (Jan 31 only)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IGeneralJournalReportReader>();

            var page = await reader.GetPageAsync(new GeneralJournalPageRequest
            {
                // GeneralJournal uses month-start boundaries (inclusive).
                FromInclusive = new DateOnly(2026, 1, 1),
                ToInclusive = new DateOnly(2026, 1, 1),
                PageSize = 100
            }, CancellationToken.None);

            page.Lines.Should().HaveCount(1);
            page.Lines[0].PeriodUtc.Should().Be(jan31);
            page.Lines[0].Amount.Should().Be(100m);
        }

        // Act + Assert (Feb 1 only)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IGeneralJournalReportReader>();

            var page = await reader.GetPageAsync(new GeneralJournalPageRequest
            {
                FromInclusive = new DateOnly(2026, 2, 1),
                ToInclusive = new DateOnly(2026, 2, 1),
                PageSize = 100
            }, CancellationToken.None);

            page.Lines.Should().HaveCount(1);
            page.Lines[0].PeriodUtc.Should().Be(feb01);
            page.Lines[0].Amount.Should().Be(200m);
        }

        // Act + Assert (range inclusive)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IGeneralJournalReportReader>();

            var page = await reader.GetPageAsync(new GeneralJournalPageRequest
            {
                FromInclusive = new DateOnly(2026, 1, 1),
                ToInclusive = new DateOnly(2026, 2, 1),
                PageSize = 100
            }, CancellationToken.None);

            page.Lines.Should().HaveCount(2);
            page.Lines[0].PeriodUtc.Should().Be(jan31);
            page.Lines[1].PeriodUtc.Should().Be(feb01);
        }
    }

    [Fact]
    public async Task AccountCard_respects_day_boundaries_inclusive()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        var d1 = Guid.CreateVersion7();
        var d2 = Guid.CreateVersion7();

        var jan31 = new DateTime(2026, 1, 31, 23, 59, 0, DateTimeKind.Utc);
        var feb01 = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        await PostCashSaleAsync(host, d1, jan31, 100m);
        await PostCashSaleAsync(host, d2, feb01, 200m);

        // Close January so AccountCard can compute opening balance for February via closed balances.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            await closing.CloseMonthAsync(new DateOnly(2026, 1, 1), closedBy: "test", CancellationToken.None);
        }

        Guid cashId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider.GetRequiredService<IChartOfAccountsProvider>();
            var chart = await provider.GetAsync(CancellationToken.None);
            cashId = chart.Get(Cash).Id;
        }

        // Act + Assert (Jan 31 only)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAccountCardEffectivePagedReportReader>();

            var page = await reader.GetPageAsync(new AccountCardReportPageRequest
            {
                AccountId = cashId,
                // AccountCard uses month-start boundaries (inclusive).
                FromInclusive = new DateOnly(2026, 1, 1),
                ToInclusive = new DateOnly(2026, 1, 1),
                PageSize = 100
            }, CancellationToken.None);

            page.OpeningBalance.Should().Be(0m);
            page.TotalDebit.Should().Be(100m);
            page.TotalCredit.Should().Be(0m);
            page.ClosingBalance.Should().Be(100m);

            page.Lines.Should().HaveCount(1);
            page.Lines[0].PeriodUtc.Should().Be(jan31);
            page.Lines[0].RunningBalance.Should().Be(100m);
        }

        // Act + Assert (Feb 1 only)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAccountCardEffectivePagedReportReader>();

            var page = await reader.GetPageAsync(new AccountCardReportPageRequest
            {
                AccountId = cashId,
                FromInclusive = new DateOnly(2026, 2, 1),
                ToInclusive = new DateOnly(2026, 2, 1),
                PageSize = 100
            }, CancellationToken.None);

            // January was closed, so the January closing balance becomes the opening balance for February.
            page.OpeningBalance.Should().Be(100m);
            page.TotalDebit.Should().Be(200m);
            page.TotalCredit.Should().Be(0m);
            page.ClosingBalance.Should().Be(300m);

            page.Lines.Should().HaveCount(1);
            page.Lines[0].PeriodUtc.Should().Be(feb01);
            page.Lines[0].RunningBalance.Should().Be(300m);
        }
    }

    [Fact]
    public async Task TrialBalance_respects_month_boundaries_inclusive()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        var d1 = Guid.CreateVersion7();
        var d2 = Guid.CreateVersion7();

        var jan31 = new DateTime(2026, 1, 31, 23, 59, 0, DateTimeKind.Utc);
        var feb01 = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        await PostCashSaleAsync(host, d1, jan31, 100m);
        await PostCashSaleAsync(host, d2, feb01, 200m);

        // Act + Assert (January only)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var tb = scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>();

            var rows = await tb.GetAsync(
                fromInclusive: new DateOnly(2026, 1, 1),
                toInclusive: new DateOnly(2026, 1, 1),
                CancellationToken.None);

            rows.Should().ContainSingle(r => r.AccountCode == Cash);
            rows.Single(r => r.AccountCode == Cash).DebitAmount.Should().Be(100m);

            rows.Should().ContainSingle(r => r.AccountCode == Revenue);
            rows.Single(r => r.AccountCode == Revenue).CreditAmount.Should().Be(100m);
        }

        // Act + Assert (February only)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var tb = scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>();

            var rows = await tb.GetAsync(
                fromInclusive: new DateOnly(2026, 2, 1),
                toInclusive: new DateOnly(2026, 2, 1),
                CancellationToken.None);

            rows.Should().ContainSingle(r => r.AccountCode == Cash);
            rows.Single(r => r.AccountCode == Cash).DebitAmount.Should().Be(200m);

            rows.Should().ContainSingle(r => r.AccountCode == Revenue);
            rows.Single(r => r.AccountCode == Revenue).CreditAmount.Should().Be(200m);
        }

        // Act + Assert (Jan..Feb inclusive)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var tb = scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>();

            var rows = await tb.GetAsync(
                fromInclusive: new DateOnly(2026, 1, 1),
                toInclusive: new DateOnly(2026, 2, 1),
                CancellationToken.None);

            rows.Should().ContainSingle(r => r.AccountCode == Cash);
            rows.Single(r => r.AccountCode == Cash).DebitAmount.Should().Be(300m);

            rows.Should().ContainSingle(r => r.AccountCode == Revenue);
            rows.Single(r => r.AccountCode == Revenue).CreditAmount.Should().Be(300m);
        }
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Cash,
            "Cash",
            AccountType.Asset,
            StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Revenue,
            "Revenue",
            AccountType.Income,
            StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }

    private static async Task PostCashSaleAsync(IHost host, Guid documentId, DateTime periodUtc, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                var debit = chart.Get(Cash);
                var credit = chart.Get(Revenue);

                ctx.Post(documentId, periodUtc, debit, credit, amount);
            },
            manageTransaction: true,
            ct: CancellationToken.None);
    }
}
