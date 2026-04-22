using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Dimensions;
using NGB.Runtime.Dimensions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Extensions;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Dimensions;

[Collection(PostgresCollection.Name)]
public sealed class DimensionSetService_RequiresTransaction_ForNonEmpty_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetOrCreateIdAsync_WhenBagIsNotEmpty_RequiresActiveTransaction_AndDoesNotPersistAnything()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var dimId = Guid.CreateVersion7();
        var valId = Guid.CreateVersion7();

        var bag = new DimensionBag(new[]
        {
            new DimensionValue(dimId, valId)
        });

        Guid expectedId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();

            // The deterministic ID is stable; this also lets us verify nothing is written on failure.
            expectedId = DeterministicGuid.Create($"DimensionSet|{dimId:N}={valId:N}");

            var act = () => svc.GetOrCreateIdAsync(bag, CancellationToken.None);

            await act.Should().ThrowAsync<NgbInvariantViolationException>()
                .WithMessage("This operation requires an active transaction.");
        }

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        (await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM platform_dimension_sets WHERE dimension_set_id = @id;",
                new { id = expectedId }))
            .Should().Be(0);

        (await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM platform_dimension_set_items WHERE dimension_set_id = @id;",
                new { id = expectedId }))
            .Should().Be(0);
    }
}
