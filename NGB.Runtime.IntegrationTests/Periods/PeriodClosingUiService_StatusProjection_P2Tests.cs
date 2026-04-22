using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Application.Abstractions.Services;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using NGB.Runtime.Periods;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class PeriodClosingUiService_StatusProjection_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetCalendarAsync_ProjectsBrokenChainAndOutOfSequenceClosedMonths()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), "50", "90.1", 100m);
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc), "50", "90.1", 200m);

        await InsertClosedPeriodAsync(host, new DateOnly(2026, 1, 1), "tester");
        await InsertClosedPeriodAsync(host, new DateOnly(2026, 3, 1), "tester");

        await using var scope = host.Services.CreateAsyncScope();
        var ui = scope.ServiceProvider.GetRequiredService<IPeriodClosingUiService>();
        var calendar = await ui.GetCalendarAsync(2026, CancellationToken.None);

        calendar.HasBrokenChain.Should().BeTrue();
        calendar.FirstGapPeriod.Should().Be(new DateOnly(2026, 2, 1));
        calendar.LatestClosedPeriod.Should().Be(new DateOnly(2026, 3, 1));
        calendar.NextClosablePeriod.Should().Be(new DateOnly(2026, 2, 1));

        var january = calendar.Months.Single(x => x.Period == new DateOnly(2026, 1, 1));
        january.State.Should().Be("Closed");
        january.IsClosed.Should().BeTrue();

        var february = calendar.Months.Single(x => x.Period == new DateOnly(2026, 2, 1));
        february.State.Should().Be("BlockedByLaterClosedMonths");
        february.BlockingPeriod.Should().Be(new DateOnly(2026, 3, 1));
        february.BlockingReason.Should().Be("LaterClosedMonths");

        var march = calendar.Months.Single(x => x.Period == new DateOnly(2026, 3, 1));
        march.State.Should().Be("ClosedOutOfSequence");
        march.IsClosed.Should().BeTrue();
        march.BlockingPeriod.Should().Be(new DateOnly(2026, 2, 1));
        march.BlockingReason.Should().Be("LaterClosedMonths");
    }

    [Fact]
    public async Task GetFiscalYearStatusAsync_ProjectsBlockingEarlierOpenMonth_WhenPriorMonthsAreIncomplete()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), "50", "90.1", 100m);
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc), "50", "90.1", 100m);

        await InsertClosedPeriodAsync(host, new DateOnly(2026, 1, 1), "tester");

        await using var scope = host.Services.CreateAsyncScope();
        var ui = scope.ServiceProvider.GetRequiredService<IPeriodClosingUiService>();
        var status = await ui.GetFiscalYearStatusAsync(new DateOnly(2026, 3, 1), CancellationToken.None);

        status.State.Should().Be("BlockedByEarlierOpenMonth");
        status.CanClose.Should().BeFalse();
        status.BlockingPeriod.Should().Be(new DateOnly(2026, 2, 1));
        status.BlockingReason.Should().Be("EarlierOpenMonth");
        status.EndPeriodClosed.Should().BeFalse();

        status.PriorMonths.Should().HaveCount(2);
        status.PriorMonths.Single(x => x.Period == new DateOnly(2026, 1, 1)).State.Should().Be("Closed");
        status.PriorMonths.Single(x => x.Period == new DateOnly(2026, 2, 1)).State.Should().Be("ReadyToClose");
    }

    [Fact]
    public async Task GetFiscalYearStatusAsync_ProjectsClosedRetainedEarningsAccount_WhenFiscalYearIsCompleted()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), "50", "90.1", 125m);

        Guid retainedEarningsId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            retainedEarningsId = await accounts.CreateAsync(new CreateAccountRequest(
                Code: "300",
                Name: "Retained Earnings UI",
                Type: AccountType.Equity,
                StatementSection: StatementSection.Equity), CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            await closing.CloseFiscalYearAsync(new DateOnly(2026, 1, 1), retainedEarningsId, "tester", CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var ui = scope.ServiceProvider.GetRequiredService<IPeriodClosingUiService>();
            var status = await ui.GetFiscalYearStatusAsync(new DateOnly(2026, 1, 1), CancellationToken.None);

            status.State.Should().Be("Completed");
            status.CanReopen.Should().BeTrue();
            status.ReopenWillOpenEndPeriod.Should().BeFalse();
            status.ClosedRetainedEarningsAccount.Should().NotBeNull();
            status.ClosedRetainedEarningsAccount!.AccountId.Should().Be(retainedEarningsId);
            status.ClosedRetainedEarningsAccount.Display.Should().Be("300 — Retained Earnings UI");
        }
    }

    private static async Task InsertClosedPeriodAsync(IHost host, DateOnly period, string closedBy)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);
        await uow.Connection.ExecuteAsync(
            "INSERT INTO accounting_closed_periods(period, closed_at_utc, closed_by) VALUES (@Period, @AtUtc, @ClosedBy);",
            new { Period = period, AtUtc = DateTime.UtcNow, ClosedBy = closedBy },
            transaction: uow.Transaction);
    }
}
