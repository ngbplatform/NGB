using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Dimensions;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Dimensions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Dimensions;

[Collection(PostgresCollection.Name)]
public sealed class DimensionTables_DbAppendOnlyGuards_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task PlatformDimensionSets_And_Items_AreAppendOnly_UpdateAndDeleteAreForbidden()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // Create a non-empty dimension set via the service (so the row definitely exists).
        var dimId = Guid.CreateVersion7();
        var valId = Guid.CreateVersion7();

        var bag = new DimensionBag(new[] { new DimensionValue(dimId, valId) });

        Guid dimensionSetId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var svc = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            try
            {
                // DimensionSetId creation requires the dimension definition row to exist (FK platform_dimension_set_items.dimension_id).
                await uow.Connection.ExecuteAsync(
                    "INSERT INTO platform_dimensions (dimension_id, code, name) VALUES (@id, @code, @name) ON CONFLICT (dimension_id) DO NOTHING;",
                    new { id = dimId, code = "it_dim_guard", name = "IT Dimension (Guard Test)" },
                    uow.Transaction);

                dimensionSetId = await svc.GetOrCreateIdAsync(bag, CancellationToken.None);
                dimensionSetId.Should().NotBe(Guid.Empty);

                await uow.CommitAsync(CancellationToken.None);
            }
            catch
            {
                try { await uow.RollbackAsync(CancellationToken.None); } catch { /* ignore */ }
                throw;
            }
        }

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        (await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM platform_dimension_sets WHERE dimension_set_id = @id;",
                new { id = dimensionSetId }))
            .Should().Be(1);

        (await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM platform_dimension_set_items WHERE dimension_set_id = @id;",
                new { id = dimensionSetId }))
            .Should().BeGreaterThan(0);

        // UPDATE platform_dimension_sets must be forbidden.
        var updSet = async () =>
        {
            await conn.ExecuteAsync(
                "UPDATE platform_dimension_sets SET dimension_set_id = dimension_set_id WHERE dimension_set_id = @id;",
                new { id = dimensionSetId });
        };
        (await updSet.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().BeOneOf("55000", "P0001");

        // DELETE platform_dimension_sets must be forbidden.
        var delSet = async () =>
        {
            await conn.ExecuteAsync(
                "DELETE FROM platform_dimension_sets WHERE dimension_set_id = @id;",
                new { id = dimensionSetId });
        };
        (await delSet.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().BeOneOf("55000", "P0001");

        // UPDATE platform_dimension_set_items must be forbidden.
        var updItems = async () =>
        {
            await conn.ExecuteAsync(
                "UPDATE platform_dimension_set_items SET dimension_id = dimension_id WHERE dimension_set_id = @id;",
                new { id = dimensionSetId });
        };
        (await updItems.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().BeOneOf("55000", "P0001");

        // DELETE platform_dimension_set_items must be forbidden.
        var delItems = async () =>
        {
            await conn.ExecuteAsync(
                "DELETE FROM platform_dimension_set_items WHERE dimension_set_id = @id;",
                new { id = dimensionSetId });
        };
        (await delItems.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().BeOneOf("55000", "P0001");
    }

    [Fact]
    public async Task EmptyDimensionSetRow_IsProtected_FromUpdateAndDelete()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        (await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM platform_dimension_sets WHERE dimension_set_id = @id;",
                new { id = Guid.Empty }))
            .Should().Be(1, "Guid.Empty reserved dimension set row must always exist");

        var upd = async () =>
        {
            await conn.ExecuteAsync(
                "UPDATE platform_dimension_sets SET dimension_set_id = dimension_set_id WHERE dimension_set_id = @id;",
                new { id = Guid.Empty });
        };
        (await upd.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().BeOneOf("55000", "P0001");

        var del = async () =>
        {
            await conn.ExecuteAsync(
                "DELETE FROM platform_dimension_sets WHERE dimension_set_id = @id;",
                new { id = Guid.Empty });
        };
        (await del.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().BeOneOf("55000", "P0001");
    }
}
