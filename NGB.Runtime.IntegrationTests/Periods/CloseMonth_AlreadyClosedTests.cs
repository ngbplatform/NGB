using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Periods;
using NGB.Persistence.Readers.Periods;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class CloseMonth_AlreadyClosedTests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CloseMonthAsync_twice_should_throw_and_not_change_closed_period_audit()
    {
        var period = new DateOnly(2026, 1, 1);

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
        var closedReader = scope.ServiceProvider.GetRequiredService<IClosedPeriodReader>();

        await closing.CloseMonthAsync(period, closedBy: "test", CancellationToken.None);

        var first = await closedReader.GetClosedAsync(period, period, CancellationToken.None);
        first.Should().ContainSingle();
        var firstRecord = first[0];

        var act = async () => await closing.CloseMonthAsync(period, closedBy: "test2", CancellationToken.None);

        var ex = await Assert.ThrowsAsync<PeriodAlreadyClosedException>(act);
        ex.Period.Should().Be(period);

        var second = await closedReader.GetClosedAsync(period, period, CancellationToken.None);
        second.Should().ContainSingle();
        second[0].Period.Should().Be(period);
        second[0].ClosedBy.Should().Be(firstRecord.ClosedBy);
        second[0].ClosedAtUtc.Should().Be(firstRecord.ClosedAtUtc);
    }
}
