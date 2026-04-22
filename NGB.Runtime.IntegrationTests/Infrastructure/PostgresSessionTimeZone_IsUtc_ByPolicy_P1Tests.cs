using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NGB.Persistence.UnitOfWork;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class PostgresSessionTimeZone_IsUtc_ByPolicy_P1Tests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task UnitOfWork_EnsureConnectionOpen_EnforcesSessionTimeZoneUtc_EvenIfConnectionStringSetsDifferentTimeZone()
    {
        await fixture.ResetDatabaseAsync();

        // Arrange: caller provides a non-UTC session timezone via startup options.
        var csb = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Options = "-c TimeZone=America/New_York"
        };

        using var host = IntegrationHostFactory.Create(csb.ToString());
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Act
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);
        var tz = await uow.Connection.ExecuteScalarAsync<string>("SHOW TIME ZONE;");

        // Assert
        tz.Should().Be("UTC");
    }
}
