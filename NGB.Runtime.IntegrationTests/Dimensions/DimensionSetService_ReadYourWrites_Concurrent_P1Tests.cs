using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Dimensions;
using NGB.Persistence.Dimensions;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Dimensions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Dimensions;

[Collection(PostgresCollection.Name)]
public sealed class DimensionSetService_ReadYourWrites_Concurrent_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetOrCreateId_InTransaction_ReadYourWrites_ViaReader_BeforeCommit()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var dim1 = Guid.CreateVersion7();
        var dim2 = Guid.CreateVersion7();
        var dim3 = Guid.CreateVersion7();

        await InsertDimensionsAsync(host, new[]
        {
            (dim1, "IT_RW_DEPT", "IT Dept"),
            (dim2, "IT_RW_PROJ", "IT Project"),
            (dim3, "IT_RW_PROP", "IT Property")
        });

        var v1 = Guid.CreateVersion7();
        var v2 = Guid.CreateVersion7();
        var v3 = Guid.CreateVersion7();

        // Intentionally unsorted to prove canonicalization doesn't depend on input order.
        var bag = new DimensionBag(new[]
        {
            new DimensionValue(dim3, v3),
            new DimensionValue(dim1, v1),
            new DimensionValue(dim2, v2)
        });

        var canonical = string.Join(";", bag.Items.Select(x => $"{x.DimensionId:N}={x.ValueId:N}"));
        var expectedId = DeterministicGuid.Create($"DimensionSet|{canonical}");

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var svc = sp.GetRequiredService<IDimensionSetService>();
        var reader = sp.GetRequiredService<IDimensionSetReader>();
        var uow = sp.GetRequiredService<IUnitOfWork>();

        await uow.BeginTransactionAsync(CancellationToken.None);

        var id = await svc.GetOrCreateIdAsync(bag, CancellationToken.None);
        id.Should().Be(expectedId);

        // Read-your-writes inside the SAME transaction via IDimensionSetReader (it uses the same IUnitOfWork).
        var bags = await reader.GetBagsByIdsAsync(new[] { id }, CancellationToken.None);
        bags.Should().ContainKey(id);
        bags[id].Items.Should().Equal(bag.Items);

        // Prove the items are really visible inside the transaction.
        var itemCount = await uow.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "select count(*) from platform_dimension_set_items where dimension_set_id = @id",
                new { id },
                transaction: uow.Transaction));

        itemCount.Should().Be(bag.Items.Count);

        await uow.CommitAsync(CancellationToken.None);

        // After commit, it must be visible to a fresh scope as well.
        await using var verifyScope = host.Services.CreateAsyncScope();
        var verifyReader = verifyScope.ServiceProvider.GetRequiredService<IDimensionSetReader>();

        var bagsAfter = await verifyReader.GetBagsByIdsAsync(new[] { id }, CancellationToken.None);
        bagsAfter[id].Items.Should().Equal(bag.Items);
    }

    [Fact]
    public async Task GetOrCreateId_ConcurrentCalls_EachCanReadOwnWrites_BeforeCommit_And_FinalStateIsSingleSet()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var dim1 = Guid.CreateVersion7();
        var dim2 = Guid.CreateVersion7();
        var dim3 = Guid.CreateVersion7();

        await InsertDimensionsAsync(host, new[]
        {
            (dim1, "IT_RW_DEPT", "IT Dept"),
            (dim2, "IT_RW_PROJ", "IT Project"),
            (dim3, "IT_RW_PROP", "IT Property")
        });

        var v1 = Guid.CreateVersion7();
        var v2 = Guid.CreateVersion7();
        var v3 = Guid.CreateVersion7();

        var bag = new DimensionBag(new[]
        {
            new DimensionValue(dim1, v1),
            new DimensionValue(dim2, v2),
            new DimensionValue(dim3, v3)
        });

        var canonical = string.Join(";", bag.Items.Select(x => $"{x.DimensionId:N}={x.ValueId:N}"));
        var expectedId = DeterministicGuid.Create($"DimensionSet|{canonical}");

        // Do NOT open connections/transactions before the start gate to avoid pool deadlocks.
        const int n = 32;
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, n).Select(async _ =>
        {
            await startGate.Task;

            await using var scope = host.Services.CreateAsyncScope();
            var sp = scope.ServiceProvider;

            var svc = sp.GetRequiredService<IDimensionSetService>();
            var reader = sp.GetRequiredService<IDimensionSetReader>();
            var uow = sp.GetRequiredService<IUnitOfWork>();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            await uow.BeginTransactionAsync(cts.Token);

            var id = await svc.GetOrCreateIdAsync(bag, cts.Token);
            id.Should().Be(expectedId);

            // Each participant can immediately read its own writes inside the same transaction.
            var bags = await reader.GetBagsByIdsAsync(new[] { id }, cts.Token);
            bags[id].Items.Should().Equal(bag.Items);

            await uow.CommitAsync(cts.Token);
        }).ToList();

        startGate.SetResult();
        await Task.WhenAll(tasks);

        // Verify the physical state: exactly one set row + exactly 3 items.
        await using var verifyScope = host.Services.CreateAsyncScope();
        var verifyUow = verifyScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await verifyUow.EnsureConnectionOpenAsync(CancellationToken.None);

        var setCount = await verifyUow.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "select count(*) from platform_dimension_sets where dimension_set_id = @id",
                new { id = expectedId }));

        setCount.Should().Be(1);

        var itemCount = await verifyUow.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "select count(*) from platform_dimension_set_items where dimension_set_id = @id",
                new { id = expectedId }));

        itemCount.Should().Be(bag.Items.Count);
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
