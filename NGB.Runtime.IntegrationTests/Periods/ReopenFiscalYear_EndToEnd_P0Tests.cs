using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Application.Abstractions.Services;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using NGB.Runtime.Periods;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class ReopenFiscalYear_EndToEnd_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ReopenFiscalYearAsync_RemovesCurrentCloseEffect_And_AllowsRedo_WithDifferentRetainedEarnings()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var (retained1, retained2) = await CreateRetainedEarningsAccountsAsync(host);
        var endPeriod = new DateOnly(2026, 1, 1);
        var closeDocumentId = DeterministicGuid.Create($"CloseFiscalYear|{endPeriod:yyyy-MM-dd}");

        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), "50", "90.1", 100m);
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc), "91", "50", 40m);

        await CloseFiscalYearAsync(host, endPeriod, retained1);
        await ReportingTestHelpers.CloseMonthAsync(host, endPeriod, "test");

        await ReopenFiscalYearAsync(host, endPeriod, "Redo with corrected retained earnings");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var entries = await sp.GetRequiredService<IAccountingEntryReader>()
                .GetByDocumentAsync(closeDocumentId, CancellationToken.None);
            entries.Should().BeEmpty();

            var postingPage = await sp.GetRequiredService<IPostingStateReader>().GetPageAsync(
                new PostingStatePageRequest
                {
                    DocumentId = closeDocumentId,
                    Operation = PostingOperation.CloseFiscalYear,
                    PageSize = 5
                },
                CancellationToken.None);
            postingPage.Records.Should().BeEmpty();

            var storedTurnovers = await sp.GetRequiredService<IAccountingTurnoverReader>()
                .GetForPeriodAsync(endPeriod, CancellationToken.None);
            var rebuiltTurnovers = await sp.GetRequiredService<IAccountingTurnoverAggregationReader>()
                .GetAggregatedFromRegisterAsync(endPeriod, CancellationToken.None);

            storedTurnovers.Should().BeEquivalentTo(rebuiltTurnovers, options => options.WithoutStrictOrdering());

            var status = await sp.GetRequiredService<IPeriodClosingUiService>()
                .GetFiscalYearStatusAsync(endPeriod, CancellationToken.None);

            status.State.Should().Be("Ready");
            status.CanClose.Should().BeTrue();
            status.CanReopen.Should().BeFalse();
            status.EndPeriodClosed.Should().BeFalse();
            status.ClosedRetainedEarningsAccount.Should().BeNull();
        }

        await CloseFiscalYearAsync(host, endPeriod, retained2);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var status = await scope.ServiceProvider.GetRequiredService<IPeriodClosingUiService>()
                .GetFiscalYearStatusAsync(endPeriod, CancellationToken.None);

            status.State.Should().Be("Completed");
            status.CanClose.Should().BeFalse();
            status.CanReopen.Should().BeTrue();
            status.ClosedRetainedEarningsAccount.Should().NotBeNull();
            status.ClosedRetainedEarningsAccount!.AccountId.Should().Be(retained2);
        }
    }

    private static async Task<(Guid retained1, Guid retained2)> CreateRetainedEarningsAccountsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        var retained1 = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "300",
            Name: "Retained Earnings A",
            Type: AccountType.Equity,
            StatementSection: StatementSection.Equity,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);

        var retained2 = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "301",
            Name: "Retained Earnings B",
            Type: AccountType.Equity,
            StatementSection: StatementSection.Equity,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);

        return (retained1, retained2);
    }

    private static async Task CloseFiscalYearAsync(IHost host, DateOnly endPeriod, Guid retainedEarningsId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
        await closing.CloseFiscalYearAsync(endPeriod, retainedEarningsId, "test", CancellationToken.None);
    }

    private static async Task ReopenFiscalYearAsync(IHost host, DateOnly endPeriod, string reason)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
        await closing.ReopenFiscalYearAsync(endPeriod, "test", reason, CancellationToken.None);
    }
}
