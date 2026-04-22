using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.AccountCard;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class Reports_EmptyDatasetSemanticsTests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly DateTime PeriodUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Period = DateOnly.FromDateTime(PeriodUtc);

    [Fact]
    public async Task TrialBalance_WhenNoTurnoversAndNoClosedBalances_ReturnsEmpty()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>();

        var rows = await reader.GetAsync(Period, Period, CancellationToken.None);

        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task GeneralJournal_WhenEmpty_ReturnsEmptyPage()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IGeneralJournalReportReader>();

        var page = await reader.GetPageAsync(
            new GeneralJournalPageRequest
            {
                FromInclusive = Period,
                ToInclusive = Period,
                PageSize = 100
            },
            CancellationToken.None);

        page.Lines.Should().BeEmpty();
        page.HasMore.Should().BeFalse();
        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task AccountCard_WhenNoEntries_ReturnsZeroBalancesAndNoLines()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await SeedMinimalCoAAsync(host);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAccountCardEffectivePagedReportReader>();

        var page = await reader.GetPageAsync(
            new AccountCardReportPageRequest
            {
                AccountId = cashId,
                FromInclusive = Period,
                ToInclusive = Period,
                PageSize = 50
            },
            CancellationToken.None);

        page.AccountId.Should().Be(cashId);
        page.OpeningBalance.Should().Be(0m);
        page.TotalDebit.Should().Be(0m);
        page.TotalCredit.Should().Be(0m);
        page.ClosingBalance.Should().Be(0m);

        page.Lines.Should().BeEmpty();
        page.HasMore.Should().BeFalse();
        page.NextCursor.Should().BeNull();
    }

    private static async Task<(Guid cashId, Guid revenueId, Guid expensesId)> SeedMinimalCoAAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        async Task<Guid> GetOrCreateAsync(string code, string name, AccountType type)
        {
            var existing = (await repo.GetForAdminAsync(includeDeleted: true))
                .FirstOrDefault(a => a.Account.Code == code && !a.IsDeleted);

            if (existing is not null)
            {
                if (!existing.IsActive)
                    await svc.SetActiveAsync(existing.Account.Id, true, CancellationToken.None);

                return existing.Account.Id;
            }

            return await svc.CreateAsync(
                new CreateAccountRequest(
                    Code: code,
                    Name: name,
                    Type: type,
                    IsContra: false,
                    NegativeBalancePolicy: NegativeBalancePolicy.Allow
                ),
                CancellationToken.None);
        }

        return (
            await GetOrCreateAsync("50", "Cash", AccountType.Asset),
            await GetOrCreateAsync("90.1", "Revenue", AccountType.Income),
            await GetOrCreateAsync("91", "Expenses", AccountType.Expense)
        );
    }
}
