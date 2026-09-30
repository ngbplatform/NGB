using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Catalogs;
using NGB.Runtime.UnitOfWork;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Catalogs;

[Collection(PmIntegrationCollection.Name)]
public sealed class PropertyUnitNumberReaderPerformanceTests(PmIntegrationFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Reader_returns_only_requested_active_units_for_the_selected_building()
    {
        await using var factory = new PmApiFactory(fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var reader = scope.ServiceProvider.GetRequiredService<IPropertyUnitNumberReader>();

        (await reader.GetExistingAsync(Guid.CreateVersion7(), [], default)).Should().BeEmpty();

        var buildingId = Guid.CreateVersion7();
        var otherBuildingId = Guid.CreateVersion7();
        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await uow.Connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO catalogs (id, catalog_code, is_deleted)
                VALUES
                    (@BuildingId, 'pm.property', FALSE),
                    (@OtherBuildingId, 'pm.property', FALSE);
                INSERT INTO cat_pm_property (catalog_id, kind, display, address_line1, city, state, zip)
                VALUES
                    (@BuildingId, 'Building', 'Building A', '1 Main', 'City', 'ST', '00001'),
                    (@OtherBuildingId, 'Building', 'Building B', '2 Main', 'City', 'ST', '00002');
                """,
                new { BuildingId = buildingId, OtherBuildingId = otherBuildingId },
                uow.Transaction,
                cancellationToken: ct));

            await InsertUnitAsync(Guid.CreateVersion7(), buildingId, "101", isDeleted: false, ct);
            await InsertUnitAsync(Guid.CreateVersion7(), buildingId, "102", isDeleted: true, ct);
            await InsertUnitAsync(Guid.CreateVersion7(), otherBuildingId, "103", isDeleted: false, ct);
        }, default);

        var existing = await reader.GetExistingAsync(
            buildingId,
            ["101", "101", "102", "103", "999"],
            default);

        existing.Should().Equal("101");

        async Task InsertUnitAsync(Guid id, Guid parentId, string unitNo, bool isDeleted, CancellationToken ct)
        {
            await uow.Connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO catalogs (id, catalog_code, is_deleted)
                VALUES (@Id, 'pm.property', @IsDeleted);
                INSERT INTO cat_pm_property (catalog_id, kind, parent_property_id, unit_no, display)
                VALUES (@Id, 'Unit', @ParentId, @UnitNo, @UnitNo);
                """,
                new { Id = id, ParentId = parentId, UnitNo = unitNo, IsDeleted = isDeleted },
                uow.Transaction,
                cancellationToken: ct));
        }
    }
}
