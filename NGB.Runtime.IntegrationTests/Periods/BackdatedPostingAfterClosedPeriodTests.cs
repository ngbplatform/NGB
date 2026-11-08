using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Posting.Validators;
using NGB.Accounting.PostingState;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Xunit;
using Xunit.Abstractions;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(AccountingPostgresCollection.Name)]
public sealed class BackdatedPostingAfterClosedPeriodTests(PostgresTestFixture fixture, ITestOutputHelper output)
    : IntegrationTestBase(fixture)
{
    [Fact]
    [Trait("Finding", "02-B01")]
    public async Task EarlierPosting_IsRejectedWithoutChangingLedgerOrLaterBalance()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var januaryDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var februaryDate = januaryDate.AddMonths(1);
        var march = new DateOnly(2026, 3, 1);
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), februaryDate, "50", "90.1", 100m);
        await ReportingTestHelpers.CloseMonthAsync(host, new DateOnly(2026, 2, 1));

        var earlierDocument = Guid.CreateVersion7();
        Func<Task> post = () => ReportingTestHelpers.PostAsync(
            host, earlierDocument, januaryDate, "50", "90.1", 50m);
        var rejected = await post.Should().ThrowAsync<PostingBeforeClosedPeriodException>();
        rejected.Which.Period.Should().Be(new DateOnly(2026, 1, 1));
        rejected.Which.ClosedPeriod.Should().Be(new DateOnly(2026, 2, 1));

        await using var scope = host.Services.CreateAsyncScope();
        var entries = await scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>()
            .GetByDocumentAsync(earlierDocument);
        entries.Should().BeEmpty("rejected posting must not leave any accounting entries");

        var trialBalance = await scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>()
            .GetAsync(march, march);
        var cash = trialBalance.Single(row => row.AccountCode == "50").ClosingBalance;

        output.WriteLine($"Rejected January receipt; committed February receipt: 100; March cash: {cash}.");
        cash.Should().Be(100m);
    }

    [Fact]
    public async Task MultiMonthPosting_WithEarlierAndFutureEntries_IsRejectedAtomically()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
            services.AddSingleton<IAccountingPostingValidator, RelaxedAccountingPostingValidator>());
        await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day15Utc.AddMonths(1), "50", "90.1", 100m);
        await ReportingTestHelpers.CloseMonthAsync(host, new DateOnly(2026, 2, 1));

        await using var scope = host.Services.CreateAsyncScope();
        var documentId = Guid.CreateVersion7();
        Func<Task> post = () => scope.ServiceProvider.GetRequiredService<PostingEngine>().PostAsync(
            PostingOperation.Post,
            async (context, ct) =>
            {
                var chart = await context.GetChartOfAccountsAsync(ct);
                context.Post(documentId, ReportingTestHelpers.Day15Utc.AddYears(-1), chart.Get("50"), chart.Get("90.1"), 50m);
                context.Post(documentId, ReportingTestHelpers.Day15Utc.AddMonths(2), chart.Get("50"), chart.Get("90.1"), 25m);
            });

        await post.Should().ThrowAsync<PostingBeforeClosedPeriodException>();
        var entries = await scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>().GetByDocumentAsync(documentId);
        entries.Should().BeEmpty();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        (await uow.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting_posting_state WHERE document_id = @documentId;", new { documentId }))
            .Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyGap_UnpostOrRepost_IsRejectedWithoutChangingExistingEntries(bool repost)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        var documentId = Guid.CreateVersion7();
        await ReportingTestHelpers.PostAsync(host, documentId, ReportingTestHelpers.Day15Utc, "50", "90.1", 50m);
        await ReportingTestHelpers.CloseMonthAsync(host, new DateOnly(2026, 1, 1));
        await ReportingTestHelpers.CloseMonthAsync(host, new DateOnly(2026, 2, 1));

        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync();
        // Simulate a legacy gap; production reopen does not allow removing this marker first.
        await uow.Connection.ExecuteAsync("DELETE FROM accounting_closed_periods WHERE period = '2026-01-01';");
        var reader = scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>();
        var before = await reader.GetByDocumentAsync(documentId);

        Func<Task> change = repost
            ? () => scope.ServiceProvider.GetRequiredService<RepostingService>().RepostAsync(documentId,
                async (context, ct) =>
                {
                    var chart = await context.GetChartOfAccountsAsync(ct);
                    context.Post(documentId, ReportingTestHelpers.Day15Utc, chart.Get("50"), chart.Get("90.1"), 75m);
                })
            : () => scope.ServiceProvider.GetRequiredService<UnpostingService>().UnpostAsync(documentId);

        await change.Should().ThrowAsync<PostingBeforeClosedPeriodException>();
        (await reader.GetByDocumentAsync(documentId)).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task EarlierPosting_AfterExplicitReopenAndSequentialClose_IsIncludedInLaterBalance()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        var january = new DateOnly(2026, 1, 1);
        var february = january.AddMonths(1);
        var march = february.AddMonths(1);
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day15Utc.AddMonths(1), "50", "90.1", 100m);
        await ReportingTestHelpers.CloseMonthAsync(host, february);

        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IPeriodClosingService>()
            .ReopenMonthAsync(february, "test", "Enter the earlier receipt");
        var earlierDocument = Guid.CreateVersion7();
        await ReportingTestHelpers.PostAsync(host, earlierDocument, ReportingTestHelpers.Day15Utc, "50", "90.1", 50m);
        await ReportingTestHelpers.CloseMonthAsync(host, january);
        await ReportingTestHelpers.CloseMonthAsync(host, february);

        var entries = await scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>()
            .GetByDocumentAsync(earlierDocument);
        entries.Should().ContainSingle().Which.Amount.Should().Be(50m);
        var rows = await scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>().GetAsync(march, march);
        rows.Single(row => row.AccountCode == "50").ClosingBalance.Should().Be(150m);
    }

    [Fact]
    [Trait("Finding", "02-B01-control")]
    public async Task PostingIntoExactlyClosedMonth_IsRejected()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var februaryDate = new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc);
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), februaryDate, "50", "90.1", 100m);
        await ReportingTestHelpers.CloseMonthAsync(host, new DateOnly(2026, 2, 1));

        Func<Task> post = () => ReportingTestHelpers.PostAsync(
            host, Guid.CreateVersion7(), februaryDate, "50", "90.1", 50m);

        await post.Should().ThrowAsync<PostingPeriodClosedException>();
    }
}
