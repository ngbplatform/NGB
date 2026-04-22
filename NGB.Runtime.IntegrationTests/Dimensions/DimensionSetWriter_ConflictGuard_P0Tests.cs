using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Dimensions;
using NGB.Persistence.Dimensions;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Dimensions;

[Collection(PostgresCollection.Name)]
public sealed class DimensionSetWriter_ConflictGuard_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task EnsureExistsAsync_WhenExistingItemHasDifferentValue_ThrowsNgbInvariantViolationException()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var writer = scope.ServiceProvider.GetRequiredService<IDimensionSetWriter>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var setId = Guid.CreateVersion7();
        var dimId = Guid.CreateVersion7();

        var existingValueId = Guid.CreateVersion7();
        var expectedValueId = Guid.CreateVersion7();

        await uow.BeginTransactionAsync(CancellationToken.None);
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO platform_dimensions(dimension_id, code, name) VALUES (@Id, 'DEPT', 'Department');",
            new { Id = dimId },
            transaction: uow.Transaction));

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO platform_dimension_sets(dimension_set_id) VALUES (@Id);",
            new { Id = setId },
            transaction: uow.Transaction));

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO platform_dimension_set_items(dimension_set_id, dimension_id, value_id) VALUES (@SetId, @DimId, @ValueId);",
            new { SetId = setId, DimId = dimId, ValueId = existingValueId },
            transaction: uow.Transaction));

        var act = async () => await writer.EnsureExistsAsync(
            setId,
            new[] { new DimensionValue(dimId, expectedValueId) },
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbInvariantViolationException>();
        ex.Which.ErrorCode.Should().Be(NgbInvariantViolationException.Code);

        ex.Which.Message.Should().Contain($"Dimension set '{setId}' conflict");
        ex.Which.Message.Should().Contain(dimId.ToString());
        ex.Which.Message.Should().Contain(expectedValueId.ToString());
        ex.Which.Message.Should().Contain(existingValueId.ToString());

        await uow.RollbackAsync(CancellationToken.None);
    }
}
