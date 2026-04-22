using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Runtime;
using Npgsql;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Receivables;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReceivablesSuggestFifoApply_Endpoint_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmReceivablesSuggestFifoApply_Endpoint_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SuggestLeaseFifoApply_AllocatesCreditsInReceivedOrder_ToChargesInDueOrder_AndDoesNotWriteByDefault()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            await setup.EnsureDefaultsAsync(CancellationToken.None);

            var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "P" }), CancellationToken.None);
            var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            display = "A",
            address_line1 = "A",
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

            var lease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
            {
                display = "Lease: P @ A",

                property_id = property.Id,
                start_on_utc = "2026-02-01",
                rent_amount = "1000.00"
            }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);

            var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
            var rentType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));

            var c1 = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                display = "RC-1",
                lease_id = lease.Id,
                charge_type_id = rentType.Id,
                due_on_utc = "2026-02-05",
                amount = "100.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, c1.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var c2 = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                display = "RC-2",
                lease_id = lease.Id,
                charge_type_id = rentType.Id,
                due_on_utc = "2026-02-10",
                amount = "40.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, c2.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var p1 = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                display = "RP-1",
                lease_id = lease.Id,
                received_on_utc = "2026-02-12",
                amount = "70.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, p1.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var p2 = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                display = "RP-2",
                lease_id = lease.Id,
                received_on_utc = "2026-02-13",
                amount = "60.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, p2.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var req = new ReceivablesSuggestFifoApplyRequest(
                LeaseId: lease.Id,
                PartyId: null,
                PropertyId: null,
                AsOfMonth: null,
                ToMonth: null,
                Limit: null,
                CreateDrafts: false);

            var http = await client.PostAsJsonAsync("/api/receivables/apply/fifo/suggest/lease", req);
            http.EnsureSuccessStatusCode();

            var resp = await http.Content.ReadFromJsonAsync<ReceivablesSuggestFifoApplyResponse>();
            resp.Should().NotBeNull();

            resp!.LeaseId.Should().Be(lease.Id);
            resp.TotalOutstanding.Should().Be(140m);
            resp.TotalCredit.Should().Be(130m);
            resp.TotalApplied.Should().Be(130m);
            resp.RemainingOutstanding.Should().Be(10m);
            resp.RemainingCredit.Should().Be(0m);

            resp.SuggestedApplies.Should().HaveCount(3);

            // Credits are consumed in received order: p1 then p2.
            resp.SuggestedApplies[0].CreditDocumentId.Should().Be(p1.Id);
            resp.SuggestedApplies[0].CreditDocumentType.Should().Be(PropertyManagementCodes.ReceivablePayment);
            resp.SuggestedApplies[0].ChargeDocumentId.Should().Be(c1.Id);
            resp.SuggestedApplies[0].Amount.Should().Be(70m);

            resp.SuggestedApplies[1].CreditDocumentId.Should().Be(p2.Id);
            resp.SuggestedApplies[1].ChargeDocumentId.Should().Be(c1.Id);
            resp.SuggestedApplies[1].Amount.Should().Be(30m);

            resp.SuggestedApplies[2].CreditDocumentId.Should().Be(p2.Id);
            resp.SuggestedApplies[2].ChargeDocumentId.Should().Be(c2.Id);
            resp.SuggestedApplies[2].Amount.Should().Be(30m);

            // Default mode must NOT create apply documents.
            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();
            (await conn.ExecuteScalarAsync<int>("select count(*) from doc_pm_receivable_apply;")).Should().Be(0);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task SuggestLeaseFifoApply_WhenCreateDraftsTrue_CreatesDraftApplyDocs_WithTypedHeads()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            await setup.EnsureDefaultsAsync(CancellationToken.None);

            var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "P" }), CancellationToken.None);
            var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            display = "A",
            address_line1 = "A",
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

            var lease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
            {
                display = "Lease: P @ A",

                property_id = property.Id,
                start_on_utc = "2026-02-01",
                rent_amount = "1000.00"
            }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);

            var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
            var rentType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));

            var c1 = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                display = "RC-1",
                lease_id = lease.Id,
                charge_type_id = rentType.Id,
                due_on_utc = "2026-02-05",
                amount = "100.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, c1.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var p1 = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                display = "RP-1",
                lease_id = lease.Id,
                received_on_utc = "2026-02-12",
                amount = "50.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, p1.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var req = new ReceivablesSuggestFifoApplyRequest(
                LeaseId: lease.Id,
                CreateDrafts: true);

            var http = await client.PostAsJsonAsync("/api/receivables/apply/fifo/suggest/lease", req);
            http.EnsureSuccessStatusCode();

            var resp = await http.Content.ReadFromJsonAsync<ReceivablesSuggestFifoApplyResponse>();
            resp.Should().NotBeNull();

            resp!.SuggestedApplies.Should().HaveCount(1);
            resp.SuggestedApplies[0].CreditDocumentType.Should().Be(PropertyManagementCodes.ReceivablePayment);
            resp.SuggestedApplies[0].ApplyId.Should().NotBeNull();

            var applyId = resp.SuggestedApplies[0].ApplyId!.Value;

            // Apply head is written and document is still draft.
            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            var status = await conn.ExecuteScalarAsync<short>(
                "select status from documents where id = @id;",
                new { id = applyId });

            status.Should().Be(1); // Draft

            var row = await conn.QuerySingleAsync<ApplyRow>(
                "select credit_document_id as CreditDocumentId, charge_document_id as ChargeDocumentId, amount as Amount from doc_pm_receivable_apply where document_id = @id;",
                new { id = applyId });

            row.CreditDocumentId.Should().Be(p1.Id);
            row.ChargeDocumentId.Should().Be(c1.Id);
            row.Amount.Should().Be(50m);

            // Draft applies must NOT affect open-items.
            var detailsUrl = $"/api/receivables/open-items/details?partyId={party.Id}&propertyId={property.Id}&leaseId={lease.Id}";
            var open = await client.GetFromJsonAsync<ReceivablesOpenItemsDetailsResponse>(detailsUrl);
            open.Should().NotBeNull();
            open!.TotalOutstanding.Should().Be(100m);
            open.TotalCredit.Should().Be(50m);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task SuggestLeaseFifoApply_IncludesCreditMemoSourceType()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            await setup.EnsureDefaultsAsync(CancellationToken.None);

            var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "P" }), CancellationToken.None);
            var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
            {
                kind = "Building",
                display = "B",
                address_line1 = "A",
                city = "Hoboken",
                state = "NJ",
                zip = "07030"
            }), CancellationToken.None);
            var property = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
            {
                kind = "Unit",
                parent_property_id = building.Id,
                unit_no = "102"
            }), CancellationToken.None);
            var lease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
            {
                property_id = property.Id,
                start_on_utc = "2026-02-01",
                rent_amount = "1000.00"
            }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);

            var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
            var utilityType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));

            var charge = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                lease_id = lease.Id,
                charge_type_id = utilityType.Id,
                due_on_utc = "2026-02-05",
                amount = "100.00"
            }), CancellationToken.None);
            charge = await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge.Id, CancellationToken.None);

            var creditMemo = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCreditMemo, Payload(new
            {
                lease_id = lease.Id,
                credited_on_utc = "2026-02-07",
                amount = "25.00",
                charge_type_id = utilityType.Id,
                memo = "Credit memo"
            }), CancellationToken.None);
            creditMemo = await documents.PostAsync(PropertyManagementCodes.ReceivableCreditMemo, creditMemo.Id, CancellationToken.None);

            var req = new ReceivablesSuggestFifoApplyRequest(LeaseId: lease.Id, CreateDrafts: false);
            var http = await client.PostAsJsonAsync("/api/receivables/apply/fifo/suggest/lease", req);
            http.EnsureSuccessStatusCode();

            var resp = await http.Content.ReadFromJsonAsync<ReceivablesSuggestFifoApplyResponse>();
            resp.Should().NotBeNull();
            resp!.SuggestedApplies.Should().ContainSingle(x =>
                x.CreditDocumentId == creditMemo.Id
                && x.CreditDocumentType == PropertyManagementCodes.ReceivableCreditMemo
                && x.ChargeDocumentId == charge.Id
                && x.Amount == 25m);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }


    private static RecordPayload Payload(object obj, IReadOnlyDictionary<string, RecordPartPayload>? parts = null)
    {
        var el = JsonSerializer.SerializeToElement(obj);
        var dict = new Dictionary<string, JsonElement>();
        foreach (var p in el.EnumerateObject())
            dict[p.Name] = p.Value;
        return new RecordPayload(dict, parts);
    }

    private static async Task DisposeFactoryAsync(PmApiFactory factory)
    {
        try { await factory.DisposeAsync(); }
        catch { /* ignore */ }
    }

    private sealed record ApplyRow(Guid CreditDocumentId, Guid ChargeDocumentId, decimal Amount);
}
