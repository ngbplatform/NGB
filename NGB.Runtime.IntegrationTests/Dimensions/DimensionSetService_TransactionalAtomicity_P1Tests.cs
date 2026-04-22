using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Dimensions;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Dimensions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Dimensions;

[Collection(PostgresCollection.Name)]
public sealed class DimensionSetService_TransactionalAtomicity_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetOrCreateId_WhenTransactionRollsBack_DoesNotPersistSetOrItems()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var dimId = Guid.CreateVersion7();
        var valueId = Guid.CreateVersion7();
        await InsertDimensionsAsync(host, new[] { (dimId, "DEPT", "Department") });

        var bag = new DimensionBag(new[] { new DimensionValue(dimId, valueId) });

        Guid id;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            id = await svc.GetOrCreateIdAsync(bag, CancellationToken.None);
            id.Should().NotBe(Guid.Empty);

            await uow.RollbackAsync(CancellationToken.None);
        }

        await using (var verifyScope = host.Services.CreateAsyncScope())
        {
            var verifyUow = verifyScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await verifyUow.EnsureConnectionOpenAsync(CancellationToken.None);

            var setCount = await verifyUow.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "select count(*) from platform_dimension_sets where dimension_set_id = @id",
                new { id }));

            var itemCount = await verifyUow.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "select count(*) from platform_dimension_set_items where dimension_set_id = @id",
                new { id }));

            setCount.Should().Be(0, "dimension sets must be part of the caller transaction (rollback should remove the set)");
            itemCount.Should().Be(0, "dimension set items must be part of the caller transaction (rollback should remove items)");
        }
    }

    [Fact]
    public async Task GetOrCreateId_WhenDimensionDoesNotExist_Fails_And_DoesNotPersistOrphanSet()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // NOTE: do NOT insert the dimension row; FK on platform_dimension_set_items must fail.
        var dimId = Guid.CreateVersion7();
        var valueId = Guid.CreateVersion7();
        var bag = new DimensionBag(new[] { new DimensionValue(dimId, valueId) });

        var canonical = string.Join(";", bag.Items.Select(x => $"{x.DimensionId:N}={x.ValueId:N}"));
        var expectedId = DeterministicGuid.Create($"DimensionSet|{canonical}");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            try
            {
                Func<Task> act = () => svc.GetOrCreateIdAsync(bag, CancellationToken.None);
                var ex = await act.Should().ThrowAsync<PostgresException>();
                ex.Which.SqlState.Should().Be("23503", "FK on platform_dimension_set_items(dimension_id) must prevent unknown dimensions");
            }
            finally
            {
                // After a statement-level failure, the transaction is aborted; rollback is required.
                await uow.RollbackAsync(CancellationToken.None);
            }
        }

        await using (var verifyScope = host.Services.CreateAsyncScope())
        {
            var verifyUow = verifyScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await verifyUow.EnsureConnectionOpenAsync(CancellationToken.None);

            var setCount = await verifyUow.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "select count(*) from platform_dimension_sets where dimension_set_id = @id",
                new { id = expectedId }));

            var itemCount = await verifyUow.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "select count(*) from platform_dimension_set_items where dimension_set_id = @id",
                new { id = expectedId }));

            setCount.Should().Be(0, "writer must not leave orphan platform_dimension_sets rows if items insert fails and transaction is rolled back");
            itemCount.Should().Be(0);
        }
    }

    private static async Task InsertDimensionsAsync(
        IHost host,
        IEnumerable<(Guid id, string code, string name)> dims)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.BeginTransactionAsync(CancellationToken.None);
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        foreach (var (id, code, name) in dims)
        {
            await uow.Connection.ExecuteAsync(new CommandDefinition(
                """
                insert into platform_dimensions(dimension_id, code, name, is_active, is_deleted)
                values (@id, @code, @name, true, false)
                on conflict (dimension_id) do nothing
                """,
                new { id, code, name },
                transaction: uow.Transaction));
        }

        await uow.CommitAsync(CancellationToken.None);
    }
}
