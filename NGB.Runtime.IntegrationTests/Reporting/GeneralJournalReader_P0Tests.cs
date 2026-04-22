using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class GeneralJournalReader_P0Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GeneralJournalReader_KeysetPaging_ReturnsAllLinesInStableOrder()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        // Create enough entries to require multiple pages.
        var docs = Enumerable.Range(0, 7).Select(_ => Guid.CreateVersion7()).ToArray();
        foreach (var doc in docs)
            await ReportingTestHelpers.PostAsync(host, doc, ReportingTestHelpers.Day1Utc, "50", "90.1", 10m);

        var expectedTotal = docs.Length * 10m;

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IGeneralJournalReader>();

        var baseRequest = new GeneralJournalPageRequest
        {
            FromInclusive = ReportingTestHelpers.Period,
            ToInclusive = ReportingTestHelpers.Period,
            PageSize = 3
        };

        var all = new List<GeneralJournalLine>();
        GeneralJournalCursor? cursor = null;
        var sum = 0m;

        for (var i = 0; i < 10; i++)
        {
            var requestCursor = cursor;
            var request = new GeneralJournalPageRequest
            {
                FromInclusive = baseRequest.FromInclusive,
                ToInclusive = baseRequest.ToInclusive,
                PageSize = baseRequest.PageSize,
                Cursor = cursor
            };

            var page = await reader.GetPageAsync(request, CancellationToken.None);
            GeneralJournalPagingContracts.AssertPageContracts(page, requestCursor);

            all.AddRange(page.Lines);
            sum += page.Lines.Sum(l => l.Amount);

            if (!page.HasMore)
                break;

            page.NextCursor.Should().NotBeNull();
            cursor = page.NextCursor;
        }

        all.Should().HaveCountGreaterThanOrEqualTo(7);

        // Stable order: (PeriodUtc, EntryId) ascending.
        var ordered = all.OrderBy(l => l.PeriodUtc).ThenBy(l => l.EntryId).ToArray();
        all.Should().Equal(ordered);

        // No duplicates.
        all.Select(l => l.EntryId).Distinct().Count().Should().Be(all.Count);

        // Totals match.
        sum.Should().Be(expectedTotal);
    }

    [Fact]
    public async Task GeneralJournalReader_FilterByDocumentId_ReturnsOnlyThatDocument()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var target = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();

        // IMPORTANT: Posting is idempotent per (DocumentId, Operation). Therefore, to create *multiple* journal lines
        // for the same DocumentId we must post multiple entries within a *single* posting operation.
        const int targetEntries = 7;
        const int otherEntries = 3;

        static async Task PostManyAsync(IHost host, Guid documentId, int entries)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            await posting.PostAsync(
                PostingOperation.Post,
                async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    var debit = chart.Get("50");
                    var credit = chart.Get("90.1");

                    for (var i = 0; i < entries; i++)
                        ctx.Post(documentId, ReportingTestHelpers.Day1Utc, debit, credit, 10m);
                },
                CancellationToken.None);
        }

        await PostManyAsync(host, target, targetEntries);
        await PostManyAsync(host, other, otherEntries);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IGeneralJournalReader>();

        var all = new List<GeneralJournalLine>();
        GeneralJournalCursor? cursor = null;
        var iterations = 0;

        while (true)
        {
            iterations++;
            iterations.Should().BeLessThan(10_000, "pagination must terminate");

            var requestCursor = cursor;
            var page = await reader.GetPageAsync(new GeneralJournalPageRequest
            {
                FromInclusive = ReportingTestHelpers.Period,
                ToInclusive = ReportingTestHelpers.Period,
                DocumentId = target,
                PageSize = 3,
                Cursor = cursor
            }, CancellationToken.None);

            GeneralJournalPagingContracts.AssertPageContracts(page, requestCursor);
            all.AddRange(page.Lines);

            if (!page.HasMore)
                break;

            cursor = page.NextCursor;
        }

        all.Should().HaveCount(targetEntries);
        all.Select(l => l.DocumentId).Distinct().Should().Equal([target]);

        // No duplicates and stable order.
        all.Select(l => l.EntryId).Distinct().Count().Should().Be(all.Count);
        all.Should().Equal(all.OrderBy(l => l.PeriodUtc).ThenBy(l => l.EntryId));
    }
}
