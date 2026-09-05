using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Reporting;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Reports;

[Collection(PmIntegrationCollection.Name)]
public sealed class PostgresTenantStatementReaderFullCoverageTests(PmIntegrationFixture fixture)
    : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Missing_lease_is_rejected_and_valid_lease_without_activity_returns_empty_page()
    {
        await using var factory = new PmApiFactory(fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var reader = scope.ServiceProvider.GetRequiredService<ITenantStatementReader>();
        var missingLeaseId = Guid.CreateVersion7();
        var to = new DateOnly(2026, 8, 22);

        Func<Task> missing = () => reader.GetPageAsync(
            new TenantStatementQuery(missingLeaseId, null, to, 0, 1),
            CancellationToken.None);
        (await missing.Should().ThrowAsync<NgbArgumentInvalidException>())
            .Which.ParamName.Should().Be("leaseId");

        var validLeaseId = Guid.CreateVersion7();
        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await uow.Connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO documents (id, type_code, date_utc, status)
                VALUES (@Id, @TypeCode, @DateUtc, 1);
                """,
                new
                {
                    Id = validLeaseId,
                    TypeCode = PropertyManagementCodes.Lease,
                    DateUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                transaction: uow.Transaction,
                cancellationToken: ct));
        }, CancellationToken.None);

        var page = await reader.GetPageAsync(
            new TenantStatementQuery(validLeaseId, DateOnly.MinValue, to, 0, 1),
            CancellationToken.None);

        page.Total.Should().Be(0);
        page.Rows.Should().BeEmpty();
        page.Totals.Should().Be(new TenantStatementTotals(DateOnly.MinValue, to, 0m, 0m, 0m, 0m));

        var cursorPage = await reader.GetCursorPageAsync(
            new TenantStatementQuery(validLeaseId, DateOnly.MinValue, to, 0, 1),
            new TenantStatementPageCursor(0, page.Total, page.Totals),
            CancellationToken.None);
        cursorPage.Total.Should().Be(0);
        cursorPage.Rows.Should().BeEmpty();
        cursorPage.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Cursor_paging_supports_null_partial_and_complete_seek_cursors()
    {
        await using var factory = new PmApiFactory(fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var reader = scope.ServiceProvider.GetRequiredService<ITenantStatementReader>();
        var leaseId = Guid.CreateVersion7();
        var buildingId = Guid.CreateVersion7();
        var propertyId = Guid.CreateVersion7();
        var partyId = Guid.CreateVersion7();
        var firstChargeId = Guid.CreateVersion7();
        var secondChargeId = Guid.CreateVersion7();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await uow.Connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO catalogs (id, catalog_code)
                VALUES (@BuildingId, @PropertyType), (@PropertyId, @PropertyType);
                INSERT INTO catalogs (id, catalog_code)
                VALUES (@PartyId, @PartyType);
                INSERT INTO cat_pm_party (catalog_id, display)
                VALUES (@PartyId, 'Statement Tenant');
                INSERT INTO cat_pm_property
                    (catalog_id, kind, display, address_line1, city, state, zip)
                VALUES
                    (@BuildingId, 'Building', 'Statement Building', '1 Test St', 'Test City', 'TS', '00000');
                INSERT INTO cat_pm_property
                    (catalog_id, kind, parent_property_id, unit_no, display)
                VALUES
                    (@PropertyId, 'Unit', @BuildingId, '101', 'Statement Unit 101');

                INSERT INTO documents (id, type_code, date_utc, status, posted_at_utc)
                VALUES
                    (@LeaseId, @LeaseType, TIMESTAMPTZ '2026-08-01 00:00:00Z', @Draft, NULL),
                    (@FirstChargeId, @ChargeType, TIMESTAMPTZ '2026-08-02 00:00:00Z', @Draft, NULL),
                    (@SecondChargeId, @ChargeType, TIMESTAMPTZ '2026-08-03 00:00:00Z', @Draft, NULL);

                INSERT INTO doc_pm_lease
                    (document_id, display, property_id, start_on_utc, rent_amount, due_day)
                VALUES
                    (@LeaseId, 'Statement Lease', @PropertyId, DATE '2026-08-01', 100, 1);
                INSERT INTO doc_pm_lease__parties
                    (document_id, party_id, role, is_primary, ordinal)
                VALUES
                    (@LeaseId, @PartyId, 'PrimaryTenant', TRUE, 1);

                INSERT INTO doc_pm_rent_charge
                    (document_id, display, lease_id, period_from_utc, period_to_utc, due_on_utc, amount, memo)
                VALUES
                    (@FirstChargeId, 'August rent', @LeaseId, DATE '2026-08-01', DATE '2026-08-31', DATE '2026-08-02', 100, 'First'),
                    (@SecondChargeId, 'September rent', @LeaseId, DATE '2026-09-01', DATE '2026-09-30', DATE '2026-08-03', 200, 'Second');

                UPDATE documents
                   SET status = @Posted,
                       posted_at_utc = date_utc + INTERVAL '1 hour'
                 WHERE id IN (@FirstChargeId, @SecondChargeId);
                """,
                new
                {
                    LeaseId = leaseId,
                    BuildingId = buildingId,
                    PropertyId = propertyId,
                    PartyId = partyId,
                    FirstChargeId = firstChargeId,
                    SecondChargeId = secondChargeId,
                    LeaseType = PropertyManagementCodes.Lease,
                    PropertyType = PropertyManagementCodes.Property,
                    PartyType = PropertyManagementCodes.Party,
                    ChargeType = PropertyManagementCodes.RentCharge,
                    Draft = (short)DocumentStatus.Draft,
                    Posted = (short)DocumentStatus.Posted
                },
                transaction: uow.Transaction,
                cancellationToken: ct));
        }, CancellationToken.None);

        var query = new TenantStatementQuery(leaseId, null, new DateOnly(2026, 8, 31), 0, 1);
        var first = await reader.GetCursorPageAsync(query, null, CancellationToken.None);
        first.Rows.Should().ContainSingle().Which.DocumentId.Should().Be(firstChargeId);
        first.HasMore.Should().BeTrue();

        var partialCursors = new[]
        {
            new TenantStatementPageCursor(0, first.Total, first.Totals, first.NextAfterOccurredOnUtc),
            new TenantStatementPageCursor(0, first.Total, first.Totals, first.NextAfterOccurredOnUtc, first.NextAfterSortOrder),
            new TenantStatementPageCursor(0, first.Total, first.Totals, first.NextAfterOccurredOnUtc, first.NextAfterSortOrder, first.NextAfterDocumentId)
        };
        foreach (var cursor in partialCursors)
        {
            var partial = await reader.GetCursorPageAsync(query, cursor, CancellationToken.None);
            partial.Rows.Should().ContainSingle();
        }

        var tail = await reader.GetCursorPageAsync(
            query,
            new TenantStatementPageCursor(
                0,
                first.Total,
                first.Totals,
                first.NextAfterOccurredOnUtc,
                first.NextAfterSortOrder,
                first.NextAfterDocumentId,
                first.NextRunningBalance),
            CancellationToken.None);
        tail.Rows.Should().ContainSingle().Which.DocumentId.Should().Be(secondChargeId);
        tail.HasMore.Should().BeFalse();
        tail.Total.Should().Be(2);
    }
}
