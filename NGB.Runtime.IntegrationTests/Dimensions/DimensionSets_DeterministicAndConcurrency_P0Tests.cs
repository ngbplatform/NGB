using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Dimensions;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Dimensions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Dimensions;

[Collection(PostgresCollection.Name)]
public sealed class DimensionSets_DeterministicAndConcurrency_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task EmptyBag_ReturnsEmptyId_And_EmptySetExistsInDatabase()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        var id = await svc.GetOrCreateIdAsync(DimensionBag.Empty, CancellationToken.None);
        id.Should().Be(Guid.Empty);

        var setCount = await uow.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "select count(*) from platform_dimension_sets where dimension_set_id = @id",
                new { id = Guid.Empty }));

        setCount.Should().Be(1, "Guid.Empty is reserved for the empty dimension set");

        var itemCount = await uow.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "select count(*) from platform_dimension_set_items where dimension_set_id = @id",
                new { id = Guid.Empty }));

        itemCount.Should().Be(0, "empty dimension set must have no items");
    }

    [Fact]
    public async Task NonEmptyBag_RequiresActiveTransaction()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var dimId = Guid.CreateVersion7();
        var valueId = Guid.CreateVersion7();

        await InsertDimensionsAsync(host, new[] { (dimId, "DEPT", "Department") });

        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();

        var bag = new DimensionBag(new[] { new DimensionValue(dimId, valueId) });

        Func<Task> act = () => svc.GetOrCreateIdAsync(bag, CancellationToken.None);
        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*active transaction*");
    }

    [Fact]
    public async Task GetOrCreateId_IsDeterministic_And_Idempotent()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var dim1 = Guid.CreateVersion7();
        var dim2 = Guid.CreateVersion7();
        var v1 = Guid.CreateVersion7();
        var v2 = Guid.CreateVersion7();

        await InsertDimensionsAsync(host, new[]
        {
            (dim1, "DEPT", "Department"),
            (dim2, "PROJ", "Project")
        });

        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.BeginTransactionAsync(CancellationToken.None);

        var bag = new DimensionBag(new[]
        {
            new DimensionValue(dim1, v1),
            new DimensionValue(dim2, v2)
        });

        var id1 = await svc.GetOrCreateIdAsync(bag, CancellationToken.None);
        var id2 = await svc.GetOrCreateIdAsync(bag, CancellationToken.None);

        id1.Should().Be(id2);

        // Optional: verify the deterministic algorithm is stable.
        var canonical = string.Join(";", bag.Items.Select(x => $"{x.DimensionId:N}={x.ValueId:N}"));
        var expected = DeterministicGuid.Create($"DimensionSet|{canonical}");
        id1.Should().Be(expected);

        await uow.CommitAsync(CancellationToken.None);

        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        var setCount = await uow.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "select count(*) from platform_dimension_sets where dimension_set_id = @id",
                new { id = id1 }));

        setCount.Should().Be(1);

        var items = (await uow.Connection.QueryAsync<(Guid dimension_id, Guid value_id)>(
            new CommandDefinition(
                "select dimension_id, value_id from platform_dimension_set_items where dimension_set_id = @id order by dimension_id",
                new { id = id1 }))).ToList();

        items.Should().HaveCount(2);
        items.Select(x => x.dimension_id).Should().BeEquivalentTo(new[] { dim1, dim2 });
    }

    [Fact]
    public async Task GetOrCreateId_ConcurrentCalls_CreateSingleSet_And_Items()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var dim1 = Guid.CreateVersion7();
        var dim2 = Guid.CreateVersion7();
        var v1 = Guid.CreateVersion7();
        var v2 = Guid.CreateVersion7();

        await InsertDimensionsAsync(host, new[]
        {
            (dim1, "DEPT", "Department"),
            (dim2, "PROJ", "Project")
        });

        var bag = new DimensionBag(new[]
        {
            new DimensionValue(dim1, v1),
            new DimensionValue(dim2, v2)
        });

        var canonical = string.Join(";", bag.Items.Select(x => $"{x.DimensionId:N}={x.ValueId:N}"));
        var expectedId = DeterministicGuid.Create($"DimensionSet|{canonical}");

        // IMPORTANT:
        // Do NOT require all participants to open DB connections/transactions before the start gate.
        // Otherwise this test can deadlock if the connection pool size < n (participants will hold
        // connections while waiting for others that can't acquire a connection).
        const int n = 24;

        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, n).Select(async _ =>
        {
            await startGate.Task;

            await using var scope = host.Services.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            // Bound the whole operation to avoid hanging forever in case of infra issues.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            await uow.BeginTransactionAsync(cts.Token);

            var id = await svc.GetOrCreateIdAsync(bag, cts.Token);
            id.Should().Be(expectedId);

            await uow.CommitAsync(cts.Token);
        }).ToList();

        // Release all participants at (almost) the same time.
        startGate.SetResult();

        await Task.WhenAll(tasks);

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

        itemCount.Should().Be(2);
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
