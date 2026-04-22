using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Readers.Periods;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class ClosedPeriodReader_Contracts_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetClosedAsync_WhenToLessThanFrom_ReturnsEmptyList_Defensive()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var reader = scope.ServiceProvider.GetRequiredService<IClosedPeriodReader>();

        var from = new DateOnly(2026, 2, 1);
        var to = new DateOnly(2026, 1, 1);

        var rows = await reader.GetClosedAsync(from, to, CancellationToken.None);

        rows.Should().BeEmpty("reader should be defensive when UX accidentally requests an empty range");
    }

    [Fact]
    public async Task GetClosedAsync_FromNotMonthStart_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var reader = scope.ServiceProvider.GetRequiredService<IClosedPeriodReader>();

        var act = () => reader.GetClosedAsync(
            fromInclusive: new DateOnly(2026, 1, 2),
            toInclusive: new DateOnly(2026, 2, 1),
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        ex.Which.ParamName.Should().Be("fromInclusive");
        ex.Which.Reason.Should().Contain("first day of a month");
    }

    [Fact]
    public async Task GetClosedAsync_ToNotMonthStart_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var reader = scope.ServiceProvider.GetRequiredService<IClosedPeriodReader>();

        var act = () => reader.GetClosedAsync(
            fromInclusive: new DateOnly(2026, 1, 1),
            toInclusive: new DateOnly(2026, 2, 2),
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        ex.Which.ParamName.Should().Be("toInclusive");
        ex.Which.Reason.Should().Contain("first day of a month");
    }

    [Fact]
    public async Task GetClosedAsync_ReturnsInclusiveRange_SortedByPeriodAscending()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var jan = new DateOnly(2026, 1, 1);
        var feb = new DateOnly(2026, 2, 1);
        var mar = new DateOnly(2026, 3, 1);

        // Seed rows directly (this is a reader contract test).
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();
            await InsertAsync(conn, mar, "tester");
            await InsertAsync(conn, jan, "tester");
            await InsertAsync(conn, feb, "tester");
        }

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IClosedPeriodReader>();

        var rows = await reader.GetClosedAsync(jan, mar, CancellationToken.None);

        rows.Select(r => r.Period)
            .Should().Equal([jan, feb, mar], "reader must return records ordered by Period ASC");

        rows.Should().OnlyContain(r => r.ClosedBy == "tester");

        var febOnly = await reader.GetClosedAsync(feb, feb, CancellationToken.None);
        febOnly.Should().ContainSingle(r => r.Period == feb, "range is inclusive on both ends");
    }

    private static async Task InsertAsync(NpgsqlConnection conn, DateOnly period, string closedBy)
    {
        const string sql = """
                           INSERT INTO accounting_closed_periods(period, closed_at_utc, closed_by)
                           VALUES (@p, @at, @by);
                           """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("p", period);
        cmd.Parameters.AddWithValue("at", new DateTime(period.Year, period.Month, 1, 12, 0, 0, DateTimeKind.Utc));
        cmd.Parameters.AddWithValue("by", closedBy);
        await cmd.ExecuteNonQueryAsync();
    }
}
