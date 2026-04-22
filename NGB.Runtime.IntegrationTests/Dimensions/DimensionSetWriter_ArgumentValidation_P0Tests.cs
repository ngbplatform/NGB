using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Dimensions;
using NGB.Persistence.Dimensions;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Dimensions;

[Collection(PostgresCollection.Name)]
public sealed class DimensionSetWriter_ArgumentValidation_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task EnsureExistsAsync_WhenDimensionSetIdEmpty_ThrowsNgbArgumentInvalidException()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var writer = scope.ServiceProvider.GetRequiredService<IDimensionSetWriter>();

        var items = new[] { new DimensionValue(Guid.CreateVersion7(), Guid.CreateVersion7()) };

        var act = async () => await writer.EnsureExistsAsync(Guid.Empty, items, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("dimensionSetId");
        ex.Which.ErrorCode.Should().Be(NgbArgumentInvalidException.Code);
        ex.Which.Message.Should().Contain("DimensionSetId must not be empty");
    }

    [Fact]
    public async Task EnsureExistsAsync_WhenItemsNull_ThrowsNgbArgumentRequiredException()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var writer = scope.ServiceProvider.GetRequiredService<IDimensionSetWriter>();

        var act = async () => await writer.EnsureExistsAsync(Guid.CreateVersion7(), null!, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentRequiredException>();
        ex.Which.ParamName.Should().Be("items");
        ex.Which.ErrorCode.Should().Be(NgbArgumentRequiredException.Code);
    }

    [Fact]
    public async Task EnsureExistsAsync_WhenItemsEmpty_ThrowsNgbArgumentInvalidException()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var writer = scope.ServiceProvider.GetRequiredService<IDimensionSetWriter>();

        var act = async () => await writer.EnsureExistsAsync(Guid.CreateVersion7(), Array.Empty<DimensionValue>(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("items");
        ex.Which.ErrorCode.Should().Be(NgbArgumentInvalidException.Code);
        ex.Which.Message.Should().Contain("Items must not be empty");
    }

    [Fact]
    public async Task EnsureExistsAsync_WhenDimensionDoesNotExist_FailsWithForeignKey_AndCanRollbackWithoutOrphans()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var writer = scope.ServiceProvider.GetRequiredService<IDimensionSetWriter>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var setId = Guid.CreateVersion7();
        var dimId = Guid.CreateVersion7(); // not inserted into platform_dimensions
        var valueId = Guid.CreateVersion7();

        await uow.BeginTransactionAsync(CancellationToken.None);

        var act = async () => await writer.EnsureExistsAsync(setId, new[] { new DimensionValue(dimId, valueId) }, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23503");
        ex.Which.ConstraintName.Should().Be("fk_platform_dimset_items_dimension");

        await uow.RollbackAsync(CancellationToken.None);

        // After rollback, neither the set row nor any items should remain.
        await uow.BeginTransactionAsync(CancellationToken.None);
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        var setCount = await uow.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM platform_dimension_sets WHERE dimension_set_id = @id;",
            new { id = setId },
            transaction: uow.Transaction));

        var itemCount = await uow.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM platform_dimension_set_items WHERE dimension_set_id = @id;",
            new { id = setId },
            transaction: uow.Transaction));

        setCount.Should().Be(0);
        itemCount.Should().Be(0);

        await uow.RollbackAsync(CancellationToken.None);
    }
}
