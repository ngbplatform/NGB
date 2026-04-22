using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Runtime;
using NGB.Runtime.Documents;
using Npgsql;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmDocuments_NumberingAndDisplay_Computed_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmDocuments_NumberingAndDisplay_Computed_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Metadata_Marks_Display_As_ReadOnly_For_AllPmHeadOnlyDocuments()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        await using var scope = factory.Services.CreateAsyncScope();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        foreach (var typeCode in new[]
                 {
                     PropertyManagementCodes.MaintenanceRequest,
                     PropertyManagementCodes.WorkOrder,
                     PropertyManagementCodes.WorkOrderCompletion,
                     PropertyManagementCodes.RentCharge,
                     PropertyManagementCodes.ReceivableCharge,
                     PropertyManagementCodes.ReceivablePayment,
                     PropertyManagementCodes.ReceivableReturnedPayment,
                     PropertyManagementCodes.ReceivableCreditMemo,
                     PropertyManagementCodes.PayableCharge,
                     PropertyManagementCodes.PayablePayment,
                     PropertyManagementCodes.ReceivableApply
                 })
        {
            var meta = await documents.GetTypeMetadataAsync(typeCode, CancellationToken.None);
            var display = meta.Form!.Sections
                .SelectMany(s => s.Rows)
                .SelectMany(r => r.Fields)
                .Single(f => f.Key == "display");

            display.IsReadOnly.Should().BeTrue($"{typeCode} display is DB-computed");
            display.IsRequired.Should().BeFalse($"{typeCode} display must not be required in payload");
            meta.Presentation.Should().NotBeNull();
            meta.Presentation!.ComputedDisplay.Should().BeTrue($"{typeCode} uses DB-computed display");
            meta.Presentation!.HasNumber.Should().BeTrue($"{typeCode} has an auto-generated document number");
            meta.Presentation!.HideSystemFieldsInEditor.Should().BeTrue($"{typeCode} hides system-managed fields from the editable form");
        }

        var leaseMeta = await documents.GetTypeMetadataAsync(PropertyManagementCodes.Lease, CancellationToken.None);
        leaseMeta.Presentation.Should().NotBeNull();
        leaseMeta.Presentation!.ComputedDisplay.Should().BeTrue();
        leaseMeta.Presentation!.HasNumber.Should().BeFalse();
        leaseMeta.Presentation!.HideSystemFieldsInEditor.Should().BeTrue();
    }

    [Fact]
    public async Task CreateDraft_Assigns_Number_And_Computes_Display_For_AllPmHeadOnlyDocuments()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        await using var scope = factory.Services.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "Tenant" }), CancellationToken.None);
        var vendor = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "Vendor", is_tenant = false, is_vendor = true }), CancellationToken.None);
        var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            address_line1 = "101 Main St",
            city = "Hoboken",
            state = "NJ",
            zip = "07030"
        }), CancellationToken.None);
        var property = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Unit",
            parent_property_id = building.Id,
            unit_no = "101"
        }), CancellationToken.None);
        var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
        var chargeType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));
        var payableChargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.PayableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
        var payableChargeType = payableChargeTypes.Items.Single(x => string.Equals(x.Display, "Repair", StringComparison.OrdinalIgnoreCase));

        var maintenanceCategories = await catalogs.GetPageAsync(PropertyManagementCodes.MaintenanceCategory, new PageRequestDto(0, 50, null), CancellationToken.None);
        var maintenanceCategory = maintenanceCategories.Items.Single(x => string.Equals(x.Display, "Plumbing", StringComparison.OrdinalIgnoreCase));

        var maintenanceRequest = await documents.CreateDraftAsync(
            PropertyManagementCodes.MaintenanceRequest,
            Payload(new
            {
                property_id = property.Id,
                party_id = party.Id,
                category_id = maintenanceCategory.Id,
                priority = "normal",
                subject = "Kitchen sink leak",
                description = "Water under the sink",
                requested_at_utc = "2026-03-10"
            }),
            CancellationToken.None);

        var lease = await documents.CreateDraftAsync(
            PropertyManagementCodes.Lease,
            Payload(
                new
                {
                    property_id = property.Id,
                    start_on_utc = "2026-02-01",
                    end_on_utc = "2027-01-31",
                    rent_amount = 1500.00m,
                    due_day = 5
                },
                LeaseParts.PrimaryTenant(party.Id)),
            CancellationToken.None);

        maintenanceRequest = await documents.PostAsync(PropertyManagementCodes.MaintenanceRequest, maintenanceRequest.Id, CancellationToken.None);

        var workOrder = await documents.CreateDraftAsync(
            PropertyManagementCodes.WorkOrder,
            Payload(new
            {
                request_id = maintenanceRequest.Id,
                assigned_party_id = vendor.Id,
                scope_of_work = "Inspect sink leak and replace trap if needed",
                due_by_utc = "2026-03-12",
                cost_responsibility = "owner"
            }),
            CancellationToken.None);

        workOrder = await documents.PostAsync(PropertyManagementCodes.WorkOrder, workOrder.Id, CancellationToken.None);

        var workOrderCompletion = await documents.CreateDraftAsync(
            PropertyManagementCodes.WorkOrderCompletion,
            Payload(new
            {
                work_order_id = workOrder.Id,
                closed_at_utc = "2026-03-13",
                outcome = "completed",
                resolution_notes = "Leak fixed and drain tested"
            }),
            CancellationToken.None);

        var rentCharge = await documents.CreateDraftAsync(
            PropertyManagementCodes.RentCharge,
            Payload(new
            {
                lease_id = lease.Id,
                period_from_utc = "2026-02-01",
                period_to_utc = "2026-02-28",
                due_on_utc = "2026-02-25",
                amount = 1500.00m,
                memo = "February rent"
            }),
            CancellationToken.None);

        var receivableCharge = await documents.CreateDraftAsync(
            PropertyManagementCodes.ReceivableCharge,
            Payload(new
            {
                lease_id = lease.Id,
                charge_type_id = chargeType.Id,
                due_on_utc = "2026-02-25",
                amount = 1500.00m,
                memo = "Open item charge"
            }),
            CancellationToken.None);

        var receivablePayment = await documents.CreateDraftAsync(
            PropertyManagementCodes.ReceivablePayment,
            Payload(new
            {
                lease_id = lease.Id,
                received_on_utc = "2026-03-06",
                amount = 1000.00m,
                memo = "Tenant payment"
            }),
            CancellationToken.None);

        (await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, receivablePayment.Id, CancellationToken.None))
            .Status.Should().Be(DocumentStatus.Posted);

        var receivableReturnedPayment = await documents.CreateDraftAsync(
            PropertyManagementCodes.ReceivableReturnedPayment,
            Payload(new
            {
                original_payment_id = receivablePayment.Id,
                returned_on_utc = "2026-03-07",
                amount = 250.00m,
                memo = "NSF"
            }),
            CancellationToken.None);

        var receivableCreditMemo = await documents.CreateDraftAsync(
            PropertyManagementCodes.ReceivableCreditMemo,
            Payload(new
            {
                lease_id = lease.Id,
                charge_type_id = chargeType.Id,
                credited_on_utc = "2026-03-07",
                amount = 125.00m,
                memo = "Credit note"
            }),
            CancellationToken.None);

        var payableCharge = await documents.CreateDraftAsync(
            PropertyManagementCodes.PayableCharge,
            Payload(new
            {
                party_id = vendor.Id,
                property_id = property.Id,
                charge_type_id = payableChargeType.Id,
                due_on_utc = "2026-03-08",
                amount = 88.50m,
                vendor_invoice_no = "INV-100",
                memo = "Vendor bill"
            }),
            CancellationToken.None);

        (await documents.PostAsync(PropertyManagementCodes.PayableCharge, payableCharge.Id, CancellationToken.None))
            .Status.Should().Be(DocumentStatus.Posted);

        var payablePayment = await documents.CreateDraftAsync(
            PropertyManagementCodes.PayablePayment,
            Payload(new
            {
                party_id = vendor.Id,
                property_id = property.Id,
                paid_on_utc = "2026-03-09",
                amount = 50.25m,
                memo = "Vendor payment"
            }),
            CancellationToken.None);

        var payableApply = await documents.CreateDraftAsync(
            PropertyManagementCodes.PayableApply,
            Payload(new
            {
                credit_document_id = payablePayment.Id,
                charge_document_id = payableCharge.Id,
                applied_on_utc = "2026-03-09",
                amount = 25.00m,
                memo = "Vendor apply"
            }),
            CancellationToken.None);

        var receivableApply = await documents.CreateDraftAsync(
            PropertyManagementCodes.ReceivableApply,
            Payload(new
            {
                credit_document_id = receivablePayment.Id,
                charge_document_id = receivableCharge.Id,
                applied_on_utc = "2026-03-06",
                amount = 1000.00m,
                memo = "Auto apply"
            }),
            CancellationToken.None);

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        await AssertDocumentAsync(conn, maintenanceRequest.Id, "pm.maintenance_request", "Maintenance Request", maintenanceRequest, expectedPrefix: "MR-");
        await AssertDocumentAsync(conn, workOrder.Id, "pm.work_order", "Work Order", workOrder, expectedPrefix: "WO-");
        await AssertDocumentAsync(conn, workOrderCompletion.Id, "pm.work_order_completion", "Work Order Completion", workOrderCompletion, expectedPrefix: "WOC-");
        await AssertDocumentAsync(conn, rentCharge.Id, "pm.rent_charge", "Rent Charge", rentCharge, expectedPrefix: "RC-");
        await AssertDocumentAsync(conn, receivableCharge.Id, "pm.receivable_charge", "Receivable Charge", receivableCharge, expectedPrefix: "RC-");
        await AssertDocumentAsync(conn, receivablePayment.Id, "pm.receivable_payment", "Receivable Payment", receivablePayment, expectedPrefix: "RP-");
        await AssertDocumentAsync(conn, receivableReturnedPayment.Id, "pm.receivable_returned_payment", "Receivable Returned Payment", receivableReturnedPayment, expectedPrefix: "RRP-");
        await AssertDocumentAsync(conn, receivableCreditMemo.Id, "pm.receivable_credit_memo", "Receivable Credit Memo", receivableCreditMemo, expectedPrefix: "RCM-");
        await AssertDocumentAsync(conn, payableCharge.Id, "pm.payable_charge", "Payable Charge", payableCharge, expectedPrefix: "PC-");
        await AssertDocumentAsync(conn, payablePayment.Id, "pm.payable_payment", "Payable Payment", payablePayment, expectedPrefix: "PP-");
        await AssertDocumentAsync(conn, payableApply.Id, "pm.payable_apply", "Payable Apply", payableApply, expectedPrefix: "PA-");
        await AssertDocumentAsync(conn, receivableApply.Id, "pm.receivable_apply", "Receivable Apply", receivableApply, expectedPrefix: "RA-");
    }

    [Fact]
    public async Task UpdateDraftHeader_WhenDateChanges_Refreshes_Computed_Display()
    {
        using var factory = new PmApiFactory(_fixture);

        await using var scope = factory.Services.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "Tenant" }), CancellationToken.None);
        var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            address_line1 = "101 Main St",
            city = "Hoboken",
            state = "NJ",
            zip = "07030"
        }), CancellationToken.None);
        var property = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Unit",
            parent_property_id = building.Id,
            unit_no = "101"
        }), CancellationToken.None);
        var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
        var chargeType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));

        var maintenanceCategories = await catalogs.GetPageAsync(PropertyManagementCodes.MaintenanceCategory, new PageRequestDto(0, 50, null), CancellationToken.None);
        var maintenanceCategory = maintenanceCategories.Items.Single(x => string.Equals(x.Display, "Plumbing", StringComparison.OrdinalIgnoreCase));

        var maintenanceRequest = await documents.CreateDraftAsync(
            PropertyManagementCodes.MaintenanceRequest,
            Payload(new
            {
                property_id = property.Id,
                party_id = party.Id,
                category_id = maintenanceCategory.Id,
                priority = "normal",
                subject = "Kitchen sink leak",
                description = "Water under the sink",
                requested_at_utc = "2026-03-10"
            }),
            CancellationToken.None);

        var lease = await documents.CreateDraftAsync(
            PropertyManagementCodes.Lease,
            Payload(
                new
                {
                    property_id = property.Id,
                    start_on_utc = "2026-02-01",
                    end_on_utc = "2027-01-31",
                    rent_amount = 1500.00m,
                    due_day = 5
                },
                LeaseParts.PrimaryTenant(party.Id)),
            CancellationToken.None);

        var charge = await documents.CreateDraftAsync(
            PropertyManagementCodes.ReceivableCharge,
            Payload(new
            {
                lease_id = lease.Id,
                charge_type_id = chargeType.Id,
                due_on_utc = "2026-02-25",
                amount = 1500.00m,
                memo = "Open item charge"
            }),
            CancellationToken.None);

        var newDateUtc = new DateTime(2026, 3, 6, 0, 0, 0, DateTimeKind.Utc);
        var changed = await drafts.UpdateDraftAsync(charge.Id, number: null, dateUtc: newDateUtc, manageTransaction: true, ct: CancellationToken.None);
        changed.Should().BeTrue();

        var reloaded = await documents.GetByIdAsync(PropertyManagementCodes.ReceivableCharge, charge.Id, CancellationToken.None);
        reloaded.Payload.Fields!["display"].GetString().Should().StartWith("Receivable Charge ");
        reloaded.Payload.Fields!["display"].GetString().Should().EndWith(" 3/6/2026");
    }

    private static async Task AssertDocumentAsync(
        NpgsqlConnection conn,
        Guid documentId,
        string expectedTypeCode,
        string expectedTitle,
        NGB.Contracts.Services.DocumentDto document,
        string expectedPrefix)
    {
        var row = await conn.QuerySingleAsync<DocumentHeaderRow>(
            """
            SELECT type_code AS TypeCode,
                   number    AS Number,
                   date_utc   AS DateUtc
              FROM documents
             WHERE id = @documentId;
            """,
            new { documentId });

        row.TypeCode.Should().Be(expectedTypeCode);
        row.Number.Should().NotBeNullOrWhiteSpace();
        row.Number!.Should().StartWith(expectedPrefix);

        document.Payload.Fields.Should().NotBeNull();
        document.Payload.Fields!.Should().ContainKey("display");
        document.Payload.Fields!["display"].GetString()
            .Should().Be($"{expectedTitle} {row.Number} {row.DateUtc:M/d/yyyy}");
    }

    private static RecordPayload Payload(object fields, object? parts = null)
    {
        var fieldsEl = JsonSerializer.SerializeToElement(fields);
        var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in fieldsEl.EnumerateObject())
            dict[p.Name] = p.Value.Clone();

        IReadOnlyDictionary<string, RecordPartPayload>? partsDict = null;
        if (parts is IReadOnlyDictionary<string, RecordPartPayload> directParts)
        {
            partsDict = directParts;
        }
        else if (parts is not null)
        {
            var partsEl = JsonSerializer.SerializeToElement(parts);
            var built = new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in partsEl.EnumerateObject())
            {
                var rows = part.Value.GetProperty("rows")
                    .EnumerateArray()
                    .Select(row =>
                    {
                        var rowDict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                        foreach (var cell in row.EnumerateObject())
                            rowDict[cell.Name] = cell.Value.Clone();
                        return (IReadOnlyDictionary<string, JsonElement>)rowDict;
                    })
                    .ToArray();
                built[part.Name] = new RecordPartPayload(rows);
            }

            partsDict = built;
        }

        return new RecordPayload(dict, partsDict);
    }

    private sealed record DocumentHeaderRow(string TypeCode, string? Number, DateTime DateUtc);
}
