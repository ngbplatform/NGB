using Dapper;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class DatabaseDefaults_Timestamptz_NoSessionTimeZoneDrift_P0Tests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task PlatformDimensionSets_CreatedAtUtc_DefaultNow_IsStableEvenWhenSessionTimeZoneIsNotUtc()
    {
        await fixture.ResetDatabaseAsync();

        // Arrange: force a non-UTC session timezone to ensure DEFAULT values do not depend on session TimeZone.
        var csb = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Options = "-c TimeZone=America/New_York"
        };

        await using var conn = new NpgsqlConnection(csb.ToString());
        await conn.OpenAsync();

        // Double-check we are really in non-UTC session timezone.
        await conn.ExecuteAsync("SET TIME ZONE 'America/New_York';");
        var tz = await conn.ExecuteScalarAsync<string>("SHOW TIME ZONE;");
        tz.Should().NotBeNullOrWhiteSpace();
        tz!.Should().NotBe("UTC");

        var before = await conn.ExecuteScalarAsync<DateTime>("SELECT clock_timestamp();");

        // Act
        var id = Guid.CreateVersion7();
        var createdAtUtc = await conn.ExecuteScalarAsync<DateTime>(
            "INSERT INTO platform_dimension_sets (dimension_set_id) VALUES (@id) RETURNING created_at_utc;",
            new { id });

        var after = await conn.ExecuteScalarAsync<DateTime>("SELECT clock_timestamp();");

        // Assert
        // With the historical bug (DEFAULT NOW() AT TIME ZONE 'UTC' for TIMESTAMPTZ) this value could drift by hours
        // when the session TimeZone is not UTC. With DEFAULT NOW(), it must fall within the statement window.
        createdAtUtc.Should().BeOnOrAfter(before.AddSeconds(-2));
        createdAtUtc.Should().BeOnOrBefore(after.AddSeconds(2));
    }
}
