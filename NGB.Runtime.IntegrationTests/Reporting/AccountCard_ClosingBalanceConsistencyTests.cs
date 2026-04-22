using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.AccountCard;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class AccountCard_ClosingBalanceConsistencyTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateOnly Period = new(2026, 1, 1);
    private static readonly DateTime PeriodUtc1 = new(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodUtc2 = new(2026, 1, 31, 23, 59, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AccountCard_last_page_closing_balance_equals_last_running_balance()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await SeedMinimalCoAAsync(host);

        // Two lines in the same month; we page by 1 to force multiple pages.
        await PostAsync(host, Guid.CreateVersion7(), PeriodUtc1, "50", "90.1", 100m);
        await PostAsync(host, Guid.CreateVersion7(), PeriodUtc2, "91", "50", 40m);

        await using var scope = host.Services.CreateAsyncScope();
        var paged = scope.ServiceProvider.GetRequiredService<IAccountCardEffectivePagedReportReader>();

        var page1 = await paged.GetPageAsync(
            new AccountCardReportPageRequest
            {
                AccountId = cashId,
                FromInclusive = Period,
                ToInclusive = Period,
                PageSize = 1
            },
            CancellationToken.None);

        page1.Lines.Should().HaveCount(1);
        page1.HasMore.Should().BeTrue();
        page1.NextCursor.Should().NotBeNull();

        var page2 = await paged.GetPageAsync(
            new AccountCardReportPageRequest
            {
                AccountId = cashId,
                FromInclusive = Period,
                ToInclusive = Period,
                PageSize = 100,
                Cursor = page1.NextCursor
            },
            CancellationToken.None);

        page2.HasMore.Should().BeFalse();
        page2.NextCursor.Should().BeNull();
        page2.Lines.Should().HaveCount(1);

        // IMPORTANT CONTRACT (bugfix guard): when HasMore=false (last page), ClosingBalance must match
        // the last visible RunningBalance so the page is self-consistent.
        page2.ClosingBalance.Should().Be(page2.Lines[^1].RunningBalance);
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

    private static async Task PostAsync(
        IHost host,
        Guid documentId,
        DateTime periodUtc,
        string debitCode,
        string creditCode,
        decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            PostingOperation.Post,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(
                    documentId,
                    periodUtc,
                    chart.Get(debitCode),
                    chart.Get(creditCode),
                    amount);
            },
            CancellationToken.None);
    }
}
