using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.PostgreSql.Reporting;
using NGB.PropertyManagement.Reporting;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Reports;

[Collection(PmIntegrationCollection.Name)]
public sealed class PostgresBuildingAndOccupancyReadersFullCoverageTests(PmIntegrationFixture fixture)
    : IAsyncLifetime
{
    private static readonly DateOnly AsOf = new(2026, 8, 22);

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Readers_reject_invalid_missing_deleted_and_nonbuilding_filters()
    {
        await using var factory = new PmApiFactory(fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var buildingReader = scope.ServiceProvider.GetRequiredService<IBuildingSummaryReader>();
        var occupancyReader = scope.ServiceProvider.GetRequiredService<IOccupancySummaryReader>();

        await AssertInvalid(() => buildingReader.GetSummaryAsync(Guid.Empty, AsOf, CancellationToken.None));
        await AssertOutOfRange(() => occupancyReader.GetPageAsync(null, AsOf, 0, 0, CancellationToken.None));
        await AssertOutOfRange(() => occupancyReader.GetPageAsync(null, AsOf, -1, 1, CancellationToken.None));
        await AssertInvalid(() => occupancyReader.GetPageAsync(Guid.Empty, AsOf, 0, 1, CancellationToken.None));

        var missingId = Guid.CreateVersion7();
        await AssertInvalid(() => buildingReader.GetSummaryAsync(missingId, AsOf, CancellationToken.None));
        await AssertInvalid(() => occupancyReader.GetPageAsync(missingId, AsOf, 0, 1, CancellationToken.None));

        var deletedId = Guid.CreateVersion7();
        var unitId = Guid.CreateVersion7();
        var blankDisplayId = Guid.CreateVersion7();
        await InsertPropertyHeadsAsync(
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            deletedId,
            unitId,
            blankDisplayId);

        await AssertInvalid(() => buildingReader.GetSummaryAsync(deletedId, AsOf, CancellationToken.None));
        await AssertInvalid(() => occupancyReader.GetPageAsync(deletedId, AsOf, 0, 1, CancellationToken.None));
        await AssertInvalid(() => buildingReader.GetSummaryAsync(unitId, AsOf, CancellationToken.None));
        await AssertInvalid(() => occupancyReader.GetPageAsync(unitId, AsOf, 0, 1, CancellationToken.None));

        var normalizedDisplay = await buildingReader.GetSummaryAsync(
            blankDisplayId,
            AsOf,
            CancellationToken.None);
        normalizedDisplay.BuildingDisplay.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Occupancy_page_beyond_total_returns_empty_rows_and_preserves_totals()
    {
        await using var factory = new PmApiFactory(fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var reader = scope.ServiceProvider.GetRequiredService<IOccupancySummaryReader>();
        var buildingId = Guid.CreateVersion7();
        await InsertPropertyHeadsAsync(uow, Guid.CreateVersion7(), Guid.CreateVersion7(), buildingId);

        var firstPage = await reader.GetPageAsync(
            buildingId: null,
            asOfUtc: AsOf,
            offset: 0,
            limit: 1,
            ct: CancellationToken.None);
        var page = await reader.GetCursorPageAsync(
            buildingId: null,
            asOfUtc: AsOf,
            cursor: new OccupancySummaryPageCursor(1, firstPage.Total, firstPage.Totals),
            limit: 1,
            ct: CancellationToken.None);

        page.Total.Should().Be(1);
        page.Rows.Should().BeEmpty();
        page.Totals.Should().Be(new OccupancySummaryTotals(AsOf, 1, 0, 0));
        page.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Maintenance_filters_accept_valid_rows_and_reject_missing_deleted_or_wrong_kind_rows()
    {
        await using var factory = new PmApiFactory(fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var reader = (PostgresMaintenanceQueueReader)scope.ServiceProvider
            .GetRequiredService<IMaintenanceQueueReader>();
        var buildingId = Guid.CreateVersion7();
        var unitId = Guid.CreateVersion7();
        var categoryId = Guid.CreateVersion7();
        var partyId = Guid.CreateVersion7();
        var deletedBuildingId = Guid.CreateVersion7();
        var deletedCategoryId = Guid.CreateVersion7();
        var deletedPartyId = Guid.CreateVersion7();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await uow.Connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO catalogs (id, catalog_code, is_deleted)
                VALUES
                    (@BuildingId, @PropertyCode, FALSE),
                    (@UnitId, @PropertyCode, FALSE),
                    (@CategoryId, @CategoryCode, FALSE),
                    (@PartyId, @PartyCode, FALSE),
                    (@DeletedBuildingId, @PropertyCode, TRUE),
                    (@DeletedCategoryId, @CategoryCode, TRUE),
                    (@DeletedPartyId, @PartyCode, TRUE);

                INSERT INTO cat_pm_property
                    (catalog_id, kind, display, address_line1, city, state, zip, parent_property_id, unit_no)
                VALUES
                    (@BuildingId, 'Building', 'Building', '1 Main St', 'Test', 'TS', '00000', NULL, NULL),
                    (@UnitId, 'Unit', 'Unit 1', NULL, NULL, NULL, NULL, @BuildingId, '1'),
                    (@DeletedBuildingId, 'Building', 'Deleted', '2 Main St', 'Test', 'TS', '00000', NULL, NULL);

                INSERT INTO cat_pm_maintenance_category (catalog_id, display)
                VALUES (@CategoryId, 'Plumbing'), (@DeletedCategoryId, 'Deleted category');

                INSERT INTO cat_pm_party (catalog_id, display)
                VALUES (@PartyId, 'Technician'), (@DeletedPartyId, 'Deleted technician');
                """,
                new
                {
                    BuildingId = buildingId,
                    UnitId = unitId,
                    CategoryId = categoryId,
                    PartyId = partyId,
                    DeletedBuildingId = deletedBuildingId,
                    DeletedCategoryId = deletedCategoryId,
                    DeletedPartyId = deletedPartyId,
                    PropertyCode = PropertyManagementCodes.Property,
                    CategoryCode = PropertyManagementCodes.MaintenanceCategory,
                    PartyCode = PropertyManagementCodes.Party
                },
                transaction: uow.Transaction,
                cancellationToken: ct));
        }, CancellationToken.None);

        await reader.ValidateBuildingFilterAsync(buildingId, CancellationToken.None);
        await reader.ValidatePropertyFilterAsync(unitId, CancellationToken.None);
        await reader.ValidateCategoryFilterAsync(categoryId, CancellationToken.None);
        await reader.ValidateAssignedPartyFilterAsync(partyId, CancellationToken.None);

        await AssertInvalid(() => reader.ValidateBuildingFilterAsync(Guid.CreateVersion7(), CancellationToken.None));
        await AssertInvalid(() => reader.ValidateBuildingFilterAsync(deletedBuildingId, CancellationToken.None));
        await AssertInvalid(() => reader.ValidateBuildingFilterAsync(unitId, CancellationToken.None));
        await AssertInvalid(() => reader.ValidatePropertyFilterAsync(Guid.CreateVersion7(), CancellationToken.None));
        await AssertInvalid(() => reader.ValidatePropertyFilterAsync(deletedBuildingId, CancellationToken.None));
        await AssertInvalid(() => reader.ValidateCategoryFilterAsync(Guid.CreateVersion7(), CancellationToken.None));
        await AssertInvalid(() => reader.ValidateCategoryFilterAsync(deletedCategoryId, CancellationToken.None));
        await AssertInvalid(() => reader.ValidateAssignedPartyFilterAsync(Guid.CreateVersion7(), CancellationToken.None));
        await AssertInvalid(() => reader.ValidateAssignedPartyFilterAsync(deletedPartyId, CancellationToken.None));
    }

    [Fact]
    public async Task Maintenance_page_beyond_an_empty_result_returns_an_empty_page()
    {
        await using var factory = new PmApiFactory(fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IMaintenanceQueueReader>();

        var page = await reader.GetPageAsync(
            new MaintenanceQueueQuery(
                AsOf,
                BuildingId: null,
                PropertyId: null,
                CategoryId: null,
                AssignedPartyId: null,
                Priority: null,
                QueueState: null,
                Offset: 1,
                Limit: 1),
            CancellationToken.None);

        page.Total.Should().Be(0);
        page.Rows.Should().BeEmpty();

        var cursorPage = await reader.GetCursorPageAsync(
            new MaintenanceQueueQuery(
                AsOf,
                BuildingId: null,
                PropertyId: null,
                CategoryId: null,
                AssignedPartyId: null,
                Priority: null,
                QueueState: null,
                Offset: 0,
                Limit: 1),
            new MaintenanceQueuePageCursor(0, 0),
            CancellationToken.None);
        cursorPage.Total.Should().Be(0);
        cursorPage.Rows.Should().BeEmpty();
    }

    private static async Task InsertPropertyHeadsAsync(
        IUnitOfWork uow,
        Guid deletedId,
        Guid unitId,
        Guid blankDisplayId)
    {
        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await uow.Connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO catalogs (id, catalog_code, is_deleted)
                VALUES
                    (@DeletedId, @Code, TRUE),
                    (@UnitId, @Code, FALSE),
                    (@BlankDisplayId, @Code, FALSE);

                INSERT INTO cat_pm_property
                    (catalog_id, kind, display, address_line1, city, state, zip, parent_property_id, unit_no)
                VALUES
                    (@DeletedId, 'Building', 'Deleted building', '1 Deleted St', 'Test', 'TS', '00000', NULL, NULL),
                    (@UnitId, 'Unit', 'Not a building', NULL, NULL, NULL, NULL, @DeletedId, '101'),
                    (@BlankDisplayId, 'Building', NULL, '2 Blank St', 'Test', 'TS', '00000', NULL, NULL);
                """,
                new
                {
                    DeletedId = deletedId,
                    UnitId = unitId,
                    BlankDisplayId = blankDisplayId,
                    Code = PropertyManagementCodes.Property
                },
                transaction: uow.Transaction,
                cancellationToken: ct));
        }, CancellationToken.None);
    }

    private static async Task AssertInvalid(Func<Task> action)
        => await action.Should().ThrowAsync<NgbArgumentInvalidException>();

    private static async Task AssertOutOfRange(Func<Task> action)
        => await action.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
}
