using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Contracts.Metadata;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.BackgroundJobs;
using NGB.Runtime.UnitOfWork;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.BackgroundJobs;

[Collection(PmIntegrationCollection.Name)]
public sealed class PropertyManagementRentChargeGenerationReaderFullCoverageTests(PmIntegrationFixture fixture)
    : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Reader_FiltersStatusAndDatesAndDeduplicatesLeaseIds()
    {
        await using var factory = new PmApiFactory(fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var reader = scope.ServiceProvider.GetRequiredService<IPropertyManagementRentChargeGenerationReader>();

        (await reader.ReadExistingRentChargePeriodsAsync([], CancellationToken.None)).Should().BeEmpty();

        var includedLeaseId = Guid.CreateVersion7();
        var draftLeaseId = Guid.CreateVersion7();
        var futureLeaseId = Guid.CreateVersion7();
        var includedChargeId = Guid.CreateVersion7();
        var deletedChargeId = Guid.CreateVersion7();
        var buildingId = Guid.CreateVersion7();
        var propertyId = Guid.CreateVersion7();
        var partyId = Guid.CreateVersion7();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await uow.Connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO catalogs (id, catalog_code) VALUES (@BuildingId, @Code);
                INSERT INTO cat_pm_property
                    (catalog_id, kind, display, address_line1, city, state, zip)
                VALUES
                    (@BuildingId, 'Building', 'Reader Test Building', '1 Test St', 'Test City', 'TS', '00000');
                INSERT INTO catalogs (id, catalog_code) VALUES (@Id, @Code);
                INSERT INTO cat_pm_property
                    (catalog_id, kind, parent_property_id, unit_no, display)
                VALUES
                    (@Id, 'Unit', @BuildingId, '101', 'Reader Test Unit 101');
                INSERT INTO catalogs (id, catalog_code) VALUES (@PartyId, @PartyCode);
                INSERT INTO cat_pm_party (catalog_id, display)
                VALUES (@PartyId, 'Reader Test Tenant');
                """,
                new
                {
                    Id = propertyId,
                    BuildingId = buildingId,
                    Code = PropertyManagementCodes.Property,
                    PartyId = partyId,
                    PartyCode = PropertyManagementCodes.Party,
                },
                uow.Transaction,
                cancellationToken: ct));

            foreach (var lease in new[]
                     {
                         new { Id = includedLeaseId, Status = (short)DocumentStatus.Posted, Start = new DateOnly(2026, 1, 1), Posted = (DateTimeOffset?)DateTimeOffset.UtcNow },
                         new { Id = draftLeaseId, Status = (short)DocumentStatus.Draft, Start = new DateOnly(2026, 1, 1), Posted = (DateTimeOffset?)null },
                         new { Id = futureLeaseId, Status = (short)DocumentStatus.Posted, Start = new DateOnly(2026, 3, 1), Posted = (DateTimeOffset?)DateTimeOffset.UtcNow },
                     })
            {
                await uow.Connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO documents (id, type_code, date_utc, status)
                    VALUES (@Id, @Type, @Date, 1);
                    INSERT INTO doc_pm_lease
                        (document_id, display, property_id, start_on_utc, end_on_utc, rent_amount, due_day)
                    VALUES
                        (@Id, @Display, @PropertyId, @Start, NULL, @Rent, @DueDay);
                    INSERT INTO doc_pm_lease__parties
                        (document_id, party_id, role, is_primary, ordinal)
                    VALUES
                        (@Id, @PartyId, 'PrimaryTenant', TRUE, 1);
                    UPDATE documents
                       SET status = @Status,
                           posted_at_utc = @Posted
                     WHERE id = @Id;
                    """,
                    new
                    {
                        lease.Id,
                        Type = PropertyManagementCodes.Lease,
                        Date = DateTimeOffset.UtcNow,
                        lease.Status,
                        lease.Posted,
                        Display = $"Lease {lease.Id}",
                        PropertyId = propertyId,
                        PartyId = partyId,
                        lease.Start,
                        Rent = 1250.50m,
                        DueDay = 5,
                    },
                    uow.Transaction,
                    cancellationToken: ct));
            }

            await InsertRentChargeAsync(includedChargeId, includedLeaseId, DocumentStatus.Draft, null, ct);
            await InsertRentChargeAsync(deletedChargeId, includedLeaseId, DocumentStatus.MarkedForDeletion, DateTimeOffset.UtcNow, ct);

            var leases = await reader.ReadPostedLeasesForMonthlyRentChargeGenerationAsync(
                new DateOnly(2026, 2, 1),
                afterStartOnUtc: null,
                afterLeaseId: null,
                limit: 100,
                ct);
            leases.Should().ContainSingle().Which.Should().Match<PmRentChargeGenerationLease>(x =>
                x.LeaseId == includedLeaseId
                && x.StartOnUtc == new DateOnly(2026, 1, 1)
                && x.EndOnUtc == null
                && x.RentAmount == 1250.50m
                && x.DueDay == 5);

            var periods = await reader.ReadExistingRentChargePeriodsAsync(
                [includedLeaseId, includedLeaseId, draftLeaseId],
                ct);
            periods.Should().ContainSingle().Which.Should().Match<PmRentChargePeriodKey>(x =>
                x.LeaseId == includedLeaseId
                && x.PeriodFromUtc == new DateOnly(2026, 1, 1)
                && x.PeriodToUtc == new DateOnly(2026, 1, 31));

            async Task InsertRentChargeAsync(
                Guid id,
                Guid leaseId,
                DocumentStatus status,
                DateTimeOffset? markedAt,
                CancellationToken token)
            {
                await uow.Connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO documents
                        (id, type_code, date_utc, status)
                    VALUES
                        (@Id, @Type, @Date, 1);
                    INSERT INTO doc_pm_rent_charge
                        (document_id, display, lease_id, period_from_utc, period_to_utc, due_on_utc, amount)
                    VALUES
                        (@Id, @Display, @LeaseId, DATE '2026-01-01', DATE '2026-01-31', DATE '2026-01-05', 1250.50);
                    UPDATE documents
                       SET status = @Status,
                           marked_for_deletion_at_utc = @MarkedAt
                     WHERE id = @Id;
                    """,
                    new
                    {
                        Id = id,
                        Type = PropertyManagementCodes.RentCharge,
                        Date = DateTimeOffset.UtcNow,
                        Status = (short)status,
                        MarkedAt = markedAt,
                        Display = $"Rent {id}",
                        LeaseId = leaseId,
                    },
                    uow.Transaction,
                    cancellationToken: token));
            }
        }, CancellationToken.None);
    }
}
