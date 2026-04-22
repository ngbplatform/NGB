using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class GeneralJournal_SortingStabilityTests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly DateOnly Period = new(2026, 1, 1);

    [Fact]
    public async Task GeneralJournal_order_is_deterministic_and_sorted_by_PeriodUtc_then_EntryId()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedReportingCoAAsync(host);

        // We post in intentionally "unsorted" order by PeriodUtc.
        // Reader contract should return lines ordered by (PeriodUtc, EntryId).
        var t0 = new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc);
        var t1 = new DateTime(2026, 1, 2, 9, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 1, 2, 9, 0, 0, DateTimeKind.Utc); // same timestamp to exercise tie-break
        var t3 = new DateTime(2026, 1, 31, 23, 59, 0, DateTimeKind.Utc);

        await PostAsync(host, Guid.CreateVersion7(), t0, "50", "80", 100m);
        await PostAsync(host, Guid.CreateVersion7(), t3, "50", "90.1", 1m);
        await PostAsync(host, Guid.CreateVersion7(), t2, "91", "50", 2m);
        await PostAsync(host, Guid.CreateVersion7(), t1, "50", "90.1", 3m);

        await using var scope = host.Services.CreateAsyncScope();
        var journal = scope.ServiceProvider.GetRequiredService<IGeneralJournalReportReader>();

        var first = await journal.GetPageAsync(
            new GeneralJournalPageRequest
            {
                FromInclusive = Period,
                ToInclusive = Period,
                PageSize = 100
            },
            CancellationToken.None);

        var second = await journal.GetPageAsync(
            new GeneralJournalPageRequest
            {
                FromInclusive = Period,
                ToInclusive = Period,
                PageSize = 100
            },
            CancellationToken.None);

        first.HasMore.Should().BeFalse();
        second.HasMore.Should().BeFalse();

        // Deterministic order (same query twice yields same sequence).
        first.Lines.Select(l => l.EntryId)
            .Should().BeEquivalentTo(second.Lines.Select(l => l.EntryId), o => o.WithStrictOrdering());

        // Sorted by (PeriodUtc, EntryId).
        var lines = first.Lines.ToArray();
        lines.Should().HaveCount(4);

        for (var i = 1; i < lines.Length; i++)
        {
            var prev = lines[i - 1];
            var cur = lines[i];

            var isNonDecreasing =
                cur.PeriodUtc > prev.PeriodUtc ||
                (cur.PeriodUtc == prev.PeriodUtc && cur.EntryId.CompareTo(prev.EntryId) >= 0);

            isNonDecreasing.Should().BeTrue($"lines must be ordered by (PeriodUtc, EntryId); violation at index {i}");
        }

        // Smoke: all lines are within the requested month (report period is month-based).
        lines.Should().OnlyContain(l => AccountingPeriod.FromDateTime(l.PeriodUtc) == Period);
    }

    private static async Task SeedReportingCoAAsync(IHost host)
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

        // Required by postings in this test.
        await GetOrCreateAsync("50", "Cash", AccountType.Asset);
        await GetOrCreateAsync("80", "Owner's Equity", AccountType.Equity);
        await GetOrCreateAsync("90.1", "Revenue", AccountType.Income);
        await GetOrCreateAsync("91", "Expenses", AccountType.Expense);
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
}
