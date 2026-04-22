using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Dimensions;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Dimensions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Dimensions;

[Collection(PostgresCollection.Name)]
public sealed class DimensionSetService_Canonicalization_EdgeCases_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetOrCreateId_IgnoresInputOrder_And_CreatesSingleSet()
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

        var bagA = new DimensionBag(new[]
        {
            new DimensionValue(dim2, v2),
            new DimensionValue(dim1, v1)
        });

        var bagB = new DimensionBag(new[]
        {
            new DimensionValue(dim1, v1),
            new DimensionValue(dim2, v2)
        });

        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.BeginTransactionAsync(CancellationToken.None);

        var idA = await svc.GetOrCreateIdAsync(bagA, CancellationToken.None);
        var idB = await svc.GetOrCreateIdAsync(bagB, CancellationToken.None);

        idA.Should().Be(idB, "DimensionBag canonicalization must make input order irrelevant");

        await uow.CommitAsync(CancellationToken.None);

        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        var setCount = await uow.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "select count(*) from platform_dimension_sets where dimension_set_id = @id",
                new { id = idA }));

        setCount.Should().Be(1);

        var itemCount = await uow.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "select count(*) from platform_dimension_set_items where dimension_set_id = @id",
                new { id = idA }));

        itemCount.Should().Be(2);
    }

    [Fact]
    public async Task GetOrCreateId_IgnoresExactDuplicates_InBag()
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

        var bagWithDuplicates = new DimensionBag(new[]
        {
            new DimensionValue(dim1, v1),
            new DimensionValue(dim2, v2),
            new DimensionValue(dim1, v1),
        });

        var bagWithoutDuplicates = new DimensionBag(new[]
        {
            new DimensionValue(dim1, v1),
            new DimensionValue(dim2, v2),
        });

        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.BeginTransactionAsync(CancellationToken.None);

        var id1 = await svc.GetOrCreateIdAsync(bagWithDuplicates, CancellationToken.None);
        var id2 = await svc.GetOrCreateIdAsync(bagWithoutDuplicates, CancellationToken.None);

        id1.Should().Be(id2, "DimensionBag should de-duplicate exact duplicates");

        await uow.CommitAsync(CancellationToken.None);

        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        var itemCount = await uow.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "select count(*) from platform_dimension_set_items where dimension_set_id = @id",
                new { id = id1 }));

        itemCount.Should().Be(2);
    }

    [Fact]
    public void DimensionBag_WhenDuplicateDimensionHasDifferentValue_Throws()
    {
        var dim = Guid.CreateVersion7();
        var v1 = Guid.CreateVersion7();
        var v2 = Guid.CreateVersion7();

        Action act = () => _ = new DimensionBag(new[]
        {
            new DimensionValue(dim, v1),
            new DimensionValue(dim, v2)
        });

        act.Should().Throw<NgbArgumentInvalidException>()
            .WithMessage("*Duplicate DimensionId with different ValueId*");
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
