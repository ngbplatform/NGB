using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using NGB.Runtime.Posting;
using Xunit;
using Xunit.Abstractions;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(AccountingPostgresCollection.Name)]
public sealed class BackdatedPostingAfterClosedPeriodTests(PostgresTestFixture fixture, ITestOutputHelper output)
    : IntegrationTestBase(fixture)
{
    // Audit finding 02-B01 remains unresolved. Keep the expected accounting result explicit
    // while the policy for posting before a later closed month is being decided.
    [Fact]
    [Trait("Finding", "02-B01")]
    public async Task EarlierPosting_MustNotDisappearFromLaterTrialBalance()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var januaryDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var februaryDate = januaryDate.AddMonths(1);
        var march = new DateOnly(2026, 3, 1);
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), februaryDate, "50", "90.1", 100m);
        await ReportingTestHelpers.CloseMonthAsync(host, new DateOnly(2026, 2, 1));

        var earlierDocument = Guid.CreateVersion7();
        await ReportingTestHelpers.PostAsync(host, earlierDocument, januaryDate, "50", "90.1", 50m);

        await using var scope = host.Services.CreateAsyncScope();
        var entries = await scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>()
            .GetByDocumentAsync(earlierDocument);
        entries.Should().ContainSingle().Which.Amount.Should().Be(50m);

        var trialBalance = await scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>()
            .GetAsync(march, march);
        var cash = trialBalance.Single(row => row.AccountCode == "50").ClosingBalance;

        output.WriteLine($"Committed ledger receipts: January 50 + February 100; March cash: {cash}.");
        cash.Should().Be(150m, "both committed receipts must remain in the subsequent balance");
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
