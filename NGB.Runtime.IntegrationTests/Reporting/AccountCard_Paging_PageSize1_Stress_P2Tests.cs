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
public sealed class AccountCard_Paging_PageSize1_Stress_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime PeriodUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Period = DateOnly.FromDateTime(PeriodUtc);

    [Fact]
    public async Task AccountCard_PageSize1_RunningBalanceRemainsContinuousAcrossManyPages()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var cashId = await SeedCoAAsync(host);

        // Injection sets a baseline opening balance for the account card.
        await PostAsync(host, Guid.CreateVersion7(), PeriodUtc, "50", "80", 1000m);

        const int n = 80;
        for (var i = 1; i <= n; i++)
        {
            if (i % 2 == 1)
            {
                // revenue increases cash
                await PostAsync(host, Guid.CreateVersion7(), PeriodUtc, "50", "90.1", i);
            }
            else
            {
                // expense decreases cash
                await PostAsync(host, Guid.CreateVersion7(), PeriodUtc, "91", "50", i);
            }
        }

        var expectedLines = n + 1;

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAccountCardEffectivePagedReportReader>();

        var ids = new List<long>(expectedLines);
        AccountCardReportCursor? cursor = null;
        decimal? opening = null;
        decimal running = 0m;
        var seenAny = false;

        for (var guard = 0; guard < 10_000; guard++)
        {
            var page = await reader.GetPageAsync(
                new AccountCardReportPageRequest
                {
                    AccountId = cashId,
                    FromInclusive = Period,
                    ToInclusive = Period,
                    PageSize = 1,
                    Cursor = cursor
                },
                CancellationToken.None);

            if (!seenAny)
            {
                opening = page.OpeningBalance;
                running = page.OpeningBalance;
                seenAny = true;
            }

            if (page.Lines.Count == 0)
                break;

            page.Lines.Should().HaveCount(1);
            var line = page.Lines[0];

            ids.Add(line.EntryId);

            running += line.Delta;
            line.RunningBalance.Should().Be(running);

            if (!page.HasMore)
                break;

            cursor = page.NextCursor;
            cursor.Should().NotBeNull();
        }

        seenAny.Should().BeTrue();
        opening.Should().NotBeNull();

        ids.Should().HaveCount(expectedLines);
        ids.Distinct().Should().HaveCount(expectedLines);
        ids.Should().BeInAscendingOrder();
    }

    private static async Task<Guid> SeedCoAAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        async Task<Guid> EnsureAsync(string code, string name, AccountType type, StatementSection section)
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
                    StatementSection: section,
                    IsContra: false,
                    NegativeBalancePolicy: NegativeBalancePolicy.Allow
                ),
                CancellationToken.None);
        }

        var cashId = await EnsureAsync("50", "Cash", AccountType.Asset, StatementSection.Assets);
        await EnsureAsync("80", "Equity", AccountType.Equity, StatementSection.Equity);
        await EnsureAsync("90.1", "Revenue", AccountType.Income, StatementSection.Income);
        await EnsureAsync("91", "Expense", AccountType.Expense, StatementSection.Expenses);

        return cashId;
    }

    private static async Task PostAsync(IHost host, Guid doc, DateTime periodUtc, string debit, string credit, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            PostingOperation.Post,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(doc, periodUtc, chart.Get(debit), chart.Get(credit), amount);
            },
            CancellationToken.None);
    }
}
