using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class CloseFiscalYear_NoClosingEntriesRequiredTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateOnly YearEnd = new(2025, 12, 1);

    [Fact]
    public async Task CloseFiscalYearAsync_when_no_PL_movements_records_posting_log_but_writes_no_entries()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var retainedEarningsId = await SeedCoAAsync(host);

        // Balance-sheet only posting (no Income/Expense activity).
        var period = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        await PostAsync(host, Guid.CreateVersion7(), period, debitCode: "50", creditCode: "80", amount: 10m);

        // Close all months before the end month (end month must stay open for FY close).
        await CloseMonthsAsync(host, start: new DateOnly(2025, 1, 1), endInclusive: new DateOnly(2025, 11, 1));

        // Act: FY close should be a "no-op" in register/turnovers, but still recorded in posting_log.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            await svc.CloseFiscalYearAsync(YearEnd, retainedEarningsId, closedBy: "test", CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var postingLog = scope.ServiceProvider.GetRequiredService<IPostingStateReader>();
            var entries = scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>();

            var now = DateTime.UtcNow;
            var page = await postingLog.GetPageAsync(
                new PostingStatePageRequest
                {
                    FromUtc = now.AddMinutes(-5),
                    ToUtc = now.AddMinutes(5),
                    Operation = PostingOperation.CloseFiscalYear,
                    Status = PostingStateStatus.Completed,
                    PageSize = 10
                },
                CancellationToken.None);

            page.Records.Should().ContainSingle("FY close must be recorded even when there are no closing entries required");
            var record = page.Records.Single();

            record.DocumentId.Should().NotBe(Guid.Empty);
            record.Operation.Should().Be(PostingOperation.CloseFiscalYear);
            record.Status.Should().Be(PostingStateStatus.Completed);

            // No register entries should be written for a no-op FY close.
            var written = await entries.GetByDocumentAsync(record.DocumentId, CancellationToken.None);
            written.Should().BeEmpty();
        }

        // Idempotency: second attempt should fail as already closed for this end period.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            var act = async () => await svc.CloseFiscalYearAsync(YearEnd, retainedEarningsId, closedBy: "test", CancellationToken.None);
            await act.Should().ThrowAsync<FiscalYearAlreadyClosedException>()
                .WithMessage("*already closed*");
        }
    }

    private static async Task<Guid> SeedCoAAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        async Task<Guid> GetOrCreateAsync(string code, string name, AccountType type, StatementSection section)
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

        // Balance sheet
        await GetOrCreateAsync("50", "Cash", AccountType.Asset, StatementSection.Assets);
        await GetOrCreateAsync("80", "Owner's Equity", AccountType.Equity, StatementSection.Equity);

        // P&L (intentionally unused in this test)
        await GetOrCreateAsync("90.1", "Revenue", AccountType.Income, StatementSection.Income);
        await GetOrCreateAsync("91", "Expenses", AccountType.Expense, StatementSection.Expenses);

        // Retained earnings for FY close.
        return await GetOrCreateAsync("84", "Retained Earnings", AccountType.Equity, StatementSection.Equity);
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
                var coa = await ctx.GetChartOfAccountsAsync(ct);
                var debit = coa.Get(debitCode);
                var credit = coa.Get(creditCode);
                ctx.Post(documentId, periodUtc, debit: debit, credit: credit, amount: amount);
                await Task.CompletedTask;
            },
            manageTransaction: true,
            CancellationToken.None);
    }

    private static async Task CloseMonthsAsync(IHost host, DateOnly start, DateOnly endInclusive)
    {
        var cur = start;
        while (cur <= endInclusive)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            await svc.CloseMonthAsync(cur, closedBy: "test", CancellationToken.None);

            cur = cur.AddMonths(1);
        }
    }
}
