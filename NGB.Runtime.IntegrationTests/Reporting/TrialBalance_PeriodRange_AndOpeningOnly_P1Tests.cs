using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class TrialBalance_PeriodRange_AndOpeningOnly_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task TrialBalance_RangeAcrossTwoMonths_SumsCorrectly()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day1Utc, "50", "90.1", 100m);
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), "50", "90.1", 50m);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>();

        var rows = await reader.GetAsync(ReportingTestHelpers.Period, ReportingTestHelpers.NextPeriod, CancellationToken.None);

        var cash = rows.Single(r => r.AccountCode == "50");
        cash.ClosingBalance.Should().Be(150m);
    }

    [Fact]
    public async Task TrialBalance_AccountWithOpeningButNoMovements_IsReturnedWithClosingEqualOpening()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        // Jan: create some balance and close month.
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day1Utc, "50", "90.1", 100m);
        await ReportingTestHelpers.CloseMonthAsync(host, ReportingTestHelpers.Period);

        // Feb: no movements; close month to materialize balances.
        await ReportingTestHelpers.CloseMonthAsync(host, ReportingTestHelpers.NextPeriod);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>();

        var rows = await reader.GetAsync(ReportingTestHelpers.NextPeriod, ReportingTestHelpers.NextPeriod, CancellationToken.None);

        var cash = rows.Single(r => r.AccountCode == "50");
        cash.OpeningBalance.Should().Be(100m);
        cash.ClosingBalance.Should().Be(100m);
        cash.DebitAmount.Should().Be(0m);
        cash.CreditAmount.Should().Be(0m);
    }

    [Fact]
    public async Task TrialBalance_WithoutClosedSnapshots_ReconstructsOpeningFromHistoricalTurnovers()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day1Utc, "50", "90.1", 100m);
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), "50", "90.1", 50m);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>();

        var rows = await reader.GetAsync(ReportingTestHelpers.NextPeriod, ReportingTestHelpers.NextPeriod, CancellationToken.None);

        var cash = rows.Single(r => r.AccountCode == "50");
        cash.OpeningBalance.Should().Be(100m);
        cash.DebitAmount.Should().Be(50m);
        cash.CreditAmount.Should().Be(0m);
        cash.ClosingBalance.Should().Be(150m);

        var revenue = rows.Single(r => r.AccountCode == "90.1");
        revenue.OpeningBalance.Should().Be(-100m);
        revenue.DebitAmount.Should().Be(0m);
        revenue.CreditAmount.Should().Be(50m);
        revenue.ClosingBalance.Should().Be(-150m);
    }
}
