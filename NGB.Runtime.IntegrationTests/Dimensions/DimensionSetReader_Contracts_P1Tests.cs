using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Dimensions;
using NGB.Persistence.Dimensions;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Dimensions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Dimensions;

[Collection(PostgresCollection.Name)]
public sealed class DimensionSetReader_Contracts_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetBagsByIds_WhenIdsAreEmpty_ReturnsEmptyDictionary()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var reader = scope.ServiceProvider.GetRequiredService<IDimensionSetReader>();

        var result = await reader.GetBagsByIdsAsync(Array.Empty<Guid>(), CancellationToken.None);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBagsByIds_IncludesGuidEmpty_And_UnknownId_ReturnsEmptyBags()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var reader = scope.ServiceProvider.GetRequiredService<IDimensionSetReader>();

        var unknown = Guid.CreateVersion7();

        var result = await reader.GetBagsByIdsAsync(new[] { Guid.Empty, unknown }, CancellationToken.None);

        result.Should().ContainKey(Guid.Empty);
        result[Guid.Empty].Should().BeSameAs(DimensionBag.Empty);

        result.Should().ContainKey(unknown);
        result[unknown].IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task GetBagsByIds_DeduplicatesIds_And_ReturnsResolvedBags()
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

        var bag1 = new DimensionBag(new[] { new DimensionValue(dim1, v1) });
        var bag2 = new DimensionBag(new[] { new DimensionValue(dim2, v2) });

        Guid id1;
        Guid id2;

        await using (var createScope = host.Services.CreateAsyncScope())
        {
            var svc = createScope.ServiceProvider.GetRequiredService<IDimensionSetService>();
            var uow = createScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            id1 = await svc.GetOrCreateIdAsync(bag1, CancellationToken.None);
            id2 = await svc.GetOrCreateIdAsync(bag2, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IDimensionSetReader>();

        var result = await reader.GetBagsByIdsAsync(new[] { id1, id1, Guid.Empty, id2 }, CancellationToken.None);

        result.Keys.Should().BeEquivalentTo(new[] { Guid.Empty, id1, id2 });

        result[id1].Items.Should().Equal(new DimensionValue(dim1, v1));
        result[id2].Items.Should().Equal(new DimensionValue(dim2, v2));
    }

    [Fact]
    public async Task GetBagsByIds_WhenSetRowExistsWithoutItems_ReturnsEmptyBag_Defensive()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var orphanSetId = Guid.CreateVersion7();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            await uow.Connection.ExecuteAsync(new CommandDefinition(
                "insert into platform_dimension_sets(dimension_set_id) values (@id) on conflict (dimension_set_id) do nothing",
                new { id = orphanSetId },
                transaction: uow.Transaction));

            await uow.CommitAsync(CancellationToken.None);
        }

        await using var readScope = host.Services.CreateAsyncScope();
        var reader = readScope.ServiceProvider.GetRequiredService<IDimensionSetReader>();

        var result = await reader.GetBagsByIdsAsync(new[] { orphanSetId }, CancellationToken.None);

        result.Should().ContainKey(orphanSetId);
        result[orphanSetId].IsEmpty.Should().BeTrue();
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
