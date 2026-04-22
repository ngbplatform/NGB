using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
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
public sealed class GeneralJournal_Paging_PageSize1_Stress_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime PeriodUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Period = DateOnly.FromDateTime(PeriodUtc);

    [Fact]
    public async Task GeneralJournal_PageSize1_DoesNotSkipOrDuplicateAcrossManyPages()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await SeedCoAAsync(host);

        // One base entry + many entries with the same PeriodUtc to stress keyset pagination tie-breaker.
        await PostAsync(host, Guid.CreateVersion7(), PeriodUtc, "50", "80", 1000m);

        const int n = 120;
        for (var i = 1; i <= n; i++)
            await PostAsync(host, Guid.CreateVersion7(), PeriodUtc, "50", "90.1", i);

        var expected = n + 1;

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IGeneralJournalReader>();

        var ids = new List<long>(expected);
        GeneralJournalCursor? cursor = null;
        var iterations = 0;

        while (true)
        {
            iterations++;
            iterations.Should().BeLessThan(10_000, "pagination must terminate");

            var page = await reader.GetPageAsync(
                new GeneralJournalPageRequest
                {
                    FromInclusive = Period,
                    ToInclusive = Period,
                    PageSize = 1,
                    Cursor = cursor
                },
                CancellationToken.None);

            GeneralJournalPagingContracts.AssertPageContracts(page, cursor);

            if (page.Lines.Count == 0)
                break;

            page.Lines.Should().HaveCount(1);
            ids.Add(page.Lines[0].EntryId);

            if (!page.HasMore)
                break;

            cursor = page.NextCursor;
            cursor.Should().NotBeNull();
        }

        ids.Should().HaveCount(expected);
        ids.Distinct().Should().HaveCount(expected, "keyset pagination must not duplicate rows");

        // Sorted by (PeriodUtc, EntryId). PeriodUtc is identical for all generated rows, so EntryId must be strictly increasing.
        ids.Should().BeInAscendingOrder();
    }

    private static async Task SeedCoAAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        async Task EnsureAsync(string code, string name, AccountType type, StatementSection section)
        {
            var existing = (await repo.GetForAdminAsync(includeDeleted: true))
                .FirstOrDefault(a => a.Account.Code == code && !a.IsDeleted);

            if (existing is not null)
            {
                if (!existing.IsActive)
                    await svc.SetActiveAsync(existing.Account.Id, true, CancellationToken.None);
                return;
            }

            await svc.CreateAsync(
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

        await EnsureAsync("50", "Cash", AccountType.Asset, StatementSection.Assets);
        await EnsureAsync("80", "Equity", AccountType.Equity, StatementSection.Equity);
        await EnsureAsync("90.1", "Revenue", AccountType.Income, StatementSection.Income);
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
