using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Dimensions;
using NGB.Runtime.Dimensions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Dimensions;

[Collection(PostgresCollection.Name)]
public sealed class DimensionSetService_EmptyBag_ReturnsEmptySetWithoutTransaction_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetOrCreateIdAsync_WhenBagIsEmpty_ReturnsGuidEmpty_WithoutTransaction_AndDoesNotCreateItems()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();

            var emptyBag = new DimensionBag(Array.Empty<DimensionValue>());

            var id1 = await svc.GetOrCreateIdAsync(emptyBag, CancellationToken.None);
            var id2 = await svc.GetOrCreateIdAsync(emptyBag, CancellationToken.None);

            id1.Should().Be(Guid.Empty);
            id2.Should().Be(Guid.Empty);
        }

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        // The platform requires a reserved empty set row to exist.
        (await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM platform_dimension_sets WHERE dimension_set_id = @id;",
                new { id = Guid.Empty }))
            .Should().Be(1);

        (await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM platform_dimension_set_items WHERE dimension_set_id = @id;",
                new { id = Guid.Empty }))
            .Should().Be(0);
    }
}
