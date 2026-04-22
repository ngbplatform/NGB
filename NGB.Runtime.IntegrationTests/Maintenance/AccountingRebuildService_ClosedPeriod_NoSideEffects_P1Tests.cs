using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Balances;
using NGB.Accounting.Turnovers;
using NGB.Persistence.Readers;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using NGB.Runtime.Maintenance;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Maintenance;

[Collection(PostgresCollection.Name)]
public sealed class AccountingRebuildService_ClosedPeriod_NoSideEffects_P1Tests(PostgresTestFixture fixture)
{
    private PostgresTestFixture Fixture { get; } = fixture;

    [Fact]
    public async Task RebuildApis_WhenPeriodIsClosed_MustThrow_AndLeaveDerivedDataUnchanged()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        // Create some derived data, then close the month.
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day1Utc, "50", "90.1", 100m);
        await ReportingTestHelpers.CloseMonthAsync(host, ReportingTestHelpers.Period);

        IReadOnlyList<AccountingTurnover> beforeTurnovers;
        IReadOnlyList<AccountingBalance> beforeBalances;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var turnoverReader = scope.ServiceProvider.GetRequiredService<IAccountingTurnoverReader>();
            var balanceReader = scope.ServiceProvider.GetRequiredService<IAccountingBalanceReader>();

            beforeTurnovers = await turnoverReader.GetForPeriodAsync(ReportingTestHelpers.Period, CancellationToken.None);
            beforeBalances = await balanceReader.GetForPeriodAsync(ReportingTestHelpers.Period, CancellationToken.None);

            beforeTurnovers.Should().NotBeEmpty();
            beforeBalances.Should().NotBeEmpty();
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var rebuild = scope.ServiceProvider.GetRequiredService<IAccountingRebuildService>();
            var period = ReportingTestHelpers.Period;

            var act1 = () => rebuild.RebuildTurnoversAsync(period, CancellationToken.None);
            var act2 = () => rebuild.RebuildBalancesAsync(period, CancellationToken.None);
            var act3 = () => rebuild.RebuildMonthAsync(period, null, CancellationToken.None);
            var act4 = () => rebuild.RebuildAndVerifyAsync(period, null, CancellationToken.None);

            await act1.Should().ThrowAsync<NGB.Runtime.Maintenance.AccountingRebuildPeriodClosedException>()
                .WithMessage($"*{period:yyyy-MM-dd}*");
            await act2.Should().ThrowAsync<NGB.Runtime.Maintenance.AccountingRebuildPeriodClosedException>()
                .WithMessage($"*{period:yyyy-MM-dd}*");
            await act3.Should().ThrowAsync<NGB.Runtime.Maintenance.AccountingRebuildPeriodClosedException>()
                .WithMessage($"*{period:yyyy-MM-dd}*");
            await act4.Should().ThrowAsync<NGB.Runtime.Maintenance.AccountingRebuildPeriodClosedException>()
                .WithMessage($"*{period:yyyy-MM-dd}*");
        }

        // The key contract: closed-period rebuild must not produce side effects (no delete/overwrite).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var turnoverReader = scope.ServiceProvider.GetRequiredService<IAccountingTurnoverReader>();
            var balanceReader = scope.ServiceProvider.GetRequiredService<IAccountingBalanceReader>();

            (await turnoverReader.GetForPeriodAsync(ReportingTestHelpers.Period, CancellationToken.None))
                .Should().BeEquivalentTo(beforeTurnovers, opts => opts.WithStrictOrdering());

            (await balanceReader.GetForPeriodAsync(ReportingTestHelpers.Period, CancellationToken.None))
                .Should().BeEquivalentTo(beforeBalances, opts => opts.WithStrictOrdering());
        }
    }
}
