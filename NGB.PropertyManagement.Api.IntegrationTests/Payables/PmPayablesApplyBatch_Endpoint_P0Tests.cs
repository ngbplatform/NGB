using System.Net;
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
using NGB.PropertyManagement.Contracts.Payables;
using NGB.PropertyManagement.Runtime;
using Npgsql;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Payables;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmPayablesApplyBatch_Endpoint_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmPayablesApplyBatch_Endpoint_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ApplyBatch_CreatesAndPostsApplyDocs_AndUpdatesOpenItems()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        await using var scope = factory.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var vendor = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new
        {
            display = "Vendor Batch",
            is_vendor = true,
            is_tenant = false
        }), CancellationToken.None);

        var property = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            address_line1 = "1 Batch Way",
            city = "Hoboken",
            state = "NJ",
            zip = "07030"
        }), CancellationToken.None);

        var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.PayableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
        var repairType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Repair", StringComparison.OrdinalIgnoreCase));

        var c1 = await documents.CreateDraftAsync(PropertyManagementCodes.PayableCharge, Payload(new
        {
            party_id = vendor.Id,
            property_id = property.Id,
            charge_type_id = repairType.Id,
            due_on_utc = "2026-03-05",
            amount = "100.00",
            vendor_invoice_no = "INV-B1"
        }), CancellationToken.None);
        (await documents.PostAsync(PropertyManagementCodes.PayableCharge, c1.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

        var c2 = await documents.CreateDraftAsync(PropertyManagementCodes.PayableCharge, Payload(new
        {
            party_id = vendor.Id,
            property_id = property.Id,
            charge_type_id = repairType.Id,
            due_on_utc = "2026-03-06",
            amount = "40.00",
            vendor_invoice_no = "INV-B2"
        }), CancellationToken.None);
        (await documents.PostAsync(PropertyManagementCodes.PayableCharge, c2.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

        var creditMemo = await documents.CreateDraftAsync(PropertyManagementCodes.PayableCreditMemo, Payload(new
        {
            party_id = vendor.Id,
            property_id = property.Id,
            charge_type_id = repairType.Id,
            credited_on_utc = "2026-03-07",
            amount = "120.00",
            memo = "Vendor credit"
        }), CancellationToken.None);
        (await documents.PostAsync(PropertyManagementCodes.PayableCreditMemo, creditMemo.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

        var req = new PayablesApplyBatchRequest(
        [
            new PayablesApplyBatchItem(null, Payload(new
            {
                credit_document_id = creditMemo.Id,
                charge_document_id = c1.Id,
                applied_on_utc = "2026-03-08",
                amount = "80.00",
                memo = "Apply 1"
            })),
            new PayablesApplyBatchItem(null, Payload(new
            {
                credit_document_id = creditMemo.Id,
                charge_document_id = c2.Id,
                applied_on_utc = "2026-03-08",
                amount = "40.00",
                memo = "Apply 2"
            }))
        ]);

        using var http = await client.PostAsJsonAsync("/api/payables/apply/batch", req);
        http.EnsureSuccessStatusCode();

        var resp = await http.Content.ReadFromJsonAsync<PayablesApplyBatchResponse>();
        resp.Should().NotBeNull();
        resp!.TotalApplied.Should().Be(120m);
        resp.ExecutedApplies.Should().HaveCount(2);
        resp.ExecutedApplies.Should().OnlyContain(x => x.CreatedDraft);
        resp.ExecutedApplies.Select(x => x.CreditDocumentId).Should().OnlyContain(x => x == creditMemo.Id);

        foreach (var executed in resp.ExecutedApplies)
        {
            var apply = await documents.GetByIdAsync(PropertyManagementCodes.PayableApply, executed.ApplyId, CancellationToken.None);
            apply.Status.Should().Be(DocumentStatus.Posted);
        }

        await using (var conn = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await conn.OpenAsync();
            (await conn.ExecuteScalarAsync<int>("select count(*) from doc_pm_payable_apply;")).Should().Be(2);
        }

        var details = await client.GetFromJsonAsync<PayablesOpenItemsDetailsResponse>(
            $"/api/payables/open-items/details?partyId={vendor.Id}&propertyId={property.Id}");

        details.Should().NotBeNull();
        details!.TotalOutstanding.Should().Be(20m);
        details.TotalCredit.Should().Be(0m);
        details.Charges.Should().ContainSingle(x => x.ChargeDocumentId == c1.Id && x.OutstandingAmount == 20m);
        details.Credits.Should().BeEmpty();
        details.Allocations.Should().HaveCount(2);
    }

    [Fact]
    public async Task ApplyBatch_WhenAnyLineInvalid_IsAtomic_NoPartialWrites()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        await using var scope = factory.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var vendor = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new
        {
            display = "Vendor Batch 2",
            is_vendor = true,
            is_tenant = false
        }), CancellationToken.None);

        var property = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            address_line1 = "2 Batch Way",
            city = "Hoboken",
            state = "NJ",
            zip = "07030"
        }), CancellationToken.None);

        var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.PayableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
        var repairType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Repair", StringComparison.OrdinalIgnoreCase));

        var c1 = await documents.CreateDraftAsync(PropertyManagementCodes.PayableCharge, Payload(new
        {
            party_id = vendor.Id,
            property_id = property.Id,
            charge_type_id = repairType.Id,
            due_on_utc = "2026-03-05",
            amount = "100.00",
            vendor_invoice_no = "INV-B3"
        }), CancellationToken.None);
        (await documents.PostAsync(PropertyManagementCodes.PayableCharge, c1.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

        var c2 = await documents.CreateDraftAsync(PropertyManagementCodes.PayableCharge, Payload(new
        {
            party_id = vendor.Id,
            property_id = property.Id,
            charge_type_id = repairType.Id,
            due_on_utc = "2026-03-06",
            amount = "40.00",
            vendor_invoice_no = "INV-B4"
        }), CancellationToken.None);
        (await documents.PostAsync(PropertyManagementCodes.PayableCharge, c2.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

        var creditMemo = await documents.CreateDraftAsync(PropertyManagementCodes.PayableCreditMemo, Payload(new
        {
            party_id = vendor.Id,
            property_id = property.Id,
            charge_type_id = repairType.Id,
            credited_on_utc = "2026-03-07",
            amount = "120.00",
            memo = "Vendor credit"
        }), CancellationToken.None);
        (await documents.PostAsync(PropertyManagementCodes.PayableCreditMemo, creditMemo.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

        var req = new PayablesApplyBatchRequest(
        [
            new PayablesApplyBatchItem(null, Payload(new
            {
                credit_document_id = creditMemo.Id,
                charge_document_id = c1.Id,
                applied_on_utc = "2026-03-08",
                amount = "80.00"
            })),
            new PayablesApplyBatchItem(null, Payload(new
            {
                credit_document_id = creditMemo.Id,
                charge_document_id = c2.Id,
                applied_on_utc = "2026-03-08",
                amount = "50.00"
            }))
        ]);

        using var http = await client.PostAsJsonAsync("/api/payables/apply/batch", req);
        http.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using (var conn = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await conn.OpenAsync();
            (await conn.ExecuteScalarAsync<int>("select count(*) from doc_pm_payable_apply;")).Should().Be(0);
        }

        var details = await client.GetFromJsonAsync<PayablesOpenItemsDetailsResponse>(
            $"/api/payables/open-items/details?partyId={vendor.Id}&propertyId={property.Id}");

        details.Should().NotBeNull();
        details!.TotalOutstanding.Should().Be(140m);
        details.TotalCredit.Should().Be(120m);
        details.Allocations.Should().BeEmpty();
    }

    private static RecordPayload Payload(object obj)
    {
        var el = JsonSerializer.SerializeToElement(obj);
        var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in el.EnumerateObject())
            dict[p.Name] = p.Value.Clone();
        return new RecordPayload(dict, null);
    }
}
