using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class GeneralLedgerAggregated_Semantics_P1Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GeneralLedgerAggregated_EmptyAccount_ReturnsEmptyLines()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await using var scope = host.Services.CreateAsyncScope();
        var rows = await ReportingTestHelpers.ReadAllGeneralLedgerAggregatedLinesAsync(
            scope.ServiceProvider,
            cashId,
            ReportingTestHelpers.Period,
            ReportingTestHelpers.Period,
            ct: CancellationToken.None);

        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task GeneralLedgerAggregated_DebitOnlyAndCreditOnly_AggregatesCorrectly()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        // Debit cash (increase)
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day1Utc, "50", "90.1", 100m);
        // Credit cash (decrease)
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day2Utc, "91", "50", 40m);

        await using var scope = host.Services.CreateAsyncScope();
        var rows = await ReportingTestHelpers.ReadAllGeneralLedgerAggregatedLinesAsync(
            scope.ServiceProvider,
            cashId,
            ReportingTestHelpers.Period,
            ReportingTestHelpers.Period,
            ct: CancellationToken.None);

        rows.Should().NotBeEmpty();
        rows.Sum(r => r.DebitAmount).Should().Be(100m);
        rows.Sum(r => r.CreditAmount).Should().Be(40m);
    }
}
