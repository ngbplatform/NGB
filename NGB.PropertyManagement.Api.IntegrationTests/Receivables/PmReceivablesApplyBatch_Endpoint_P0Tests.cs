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
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Runtime;
using Npgsql;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Receivables;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReceivablesApplyBatch_Endpoint_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmReceivablesApplyBatch_Endpoint_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ApplyBatch_WhenItemsReferenceExistingDraftApplies_PostsAllAtomically_AndUpdatesOpenItems()
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

            // 1) Create draft applies using suggest endpoint (wizard flow).
            var suggestReq = new ReceivablesSuggestFifoApplyRequest(LeaseId: lease.Id, CreateDrafts: true);
            var suggestHttp = await client.PostAsJsonAsync("/api/receivables/apply/fifo/suggest/lease", suggestReq);
            suggestHttp.EnsureSuccessStatusCode();

            var suggest = await suggestHttp.Content.ReadFromJsonAsync<ReceivablesSuggestFifoApplyResponse>();
            suggest.Should().NotBeNull();
            suggest!.SuggestedApplies.Should().HaveCount(3);
            suggest.SuggestedApplies.All(x => x.ApplyId is not null).Should().BeTrue();

            // 2) Post them atomically via batch endpoint.
            var batchReq = new ReceivablesApplyBatchRequest(
                Applies: suggest.SuggestedApplies
                    .Select(x => new ReceivablesApplyBatchItem(x.ApplyId, x.ApplyPayload))
                    .ToArray());

            var batchHttp = await client.PostAsJsonAsync("/api/receivables/apply/batch", batchReq);
            batchHttp.EnsureSuccessStatusCode();

            var batch = await batchHttp.Content.ReadFromJsonAsync<ReceivablesApplyBatchResponse>();
            batch.Should().NotBeNull();
            batch!.ExecutedApplies.Should().HaveCount(3);
            batch.RegisterId.Should().NotBe(Guid.Empty);
            batch.TotalApplied.Should().Be(130m);

            // Docs are posted.
            foreach (var x in batch.ExecutedApplies)
            {
                x.CreatedDraft.Should().BeFalse();
                var d = await documents.GetByIdAsync(PropertyManagementCodes.ReceivableApply, x.ApplyId, CancellationToken.None);
                d.Status.Should().Be(DocumentStatus.Posted);
            }

            var detailsUrl = $"/api/receivables/open-items/details?partyId={party.Id}&propertyId={property.Id}&leaseId={lease.Id}";
            var open = await client.GetFromJsonAsync<ReceivablesOpenItemsDetailsResponse>(detailsUrl);
            open.Should().NotBeNull();

            open!.TotalOutstanding.Should().Be(10m);
            open.TotalCredit.Should().Be(0m);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();
            (await conn.ExecuteScalarAsync<int>("select count(*) from doc_pm_receivable_apply;")).Should().Be(3);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task ApplyBatch_WhenAnyApplyIsInvalid_IsAtomic_NoPartialWrites()
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

            var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                display = "RP-1",
                lease_id = lease.Id,
                received_on_utc = "2026-02-12",
                amount = "50.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            // Two applies: first is valid (30), second would exceed remaining credit (30 > 20) => should fail.
            var ok = new ReceivablesApplyBatchItem(
                ApplyId: null,
                ApplyPayload: Payload(new
                {
                    credit_document_id = payment.Id,
                    charge_document_id = c1.Id,
                    applied_on_utc = "2026-02-12",
                    amount = "30.00"
                }));

            var bad = new ReceivablesApplyBatchItem(
                ApplyId: null,
                ApplyPayload: Payload(new
                {
                    credit_document_id = payment.Id,
                    charge_document_id = c1.Id,
                    applied_on_utc = "2026-02-12",
                    amount = "30.00"
                }));

            var req = new ReceivablesApplyBatchRequest([ok, bad]);
            var http = await client.PostAsJsonAsync("/api/receivables/apply/batch", req);
            http.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // No applies should be created or posted (rollback).
            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();
            (await conn.ExecuteScalarAsync<int>("select count(*) from doc_pm_receivable_apply;")).Should().Be(0);

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
}
