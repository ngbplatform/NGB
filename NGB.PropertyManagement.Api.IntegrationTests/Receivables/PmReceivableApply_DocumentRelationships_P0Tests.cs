using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Graph;
using NGB.Contracts.Metadata;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Runtime;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Receivables;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReceivableApply_DocumentRelationships_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    // API uses JsonStringEnumConverter for enums.
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmReceivableApply_DocumentRelationships_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SuggestLease_WhenCreateDraftsTrue_WritesBasedOnRelationships_OnDraftApply()
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

            var charge = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                display = "RC-1",
                lease_id = lease.Id,
                charge_type_id = rentType.Id,
                due_on_utc = "2026-02-05",
                amount = "50.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                display = "RP-1",
                lease_id = lease.Id,
                received_on_utc = "2026-02-10",
                amount = "30.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(NGB.Contracts.Metadata.DocumentStatus.Posted);

            // Create draft applies.
            var suggestReq = new ReceivablesSuggestFifoApplyRequest(LeaseId: lease.Id, CreateDrafts: true, Limit: 10);
            using var suggestHttp = await client.PostAsJsonAsync("/api/receivables/apply/fifo/suggest/lease", suggestReq);
            suggestHttp.StatusCode.Should().Be(HttpStatusCode.OK);

            var suggest = await suggestHttp.Content.ReadFromJsonAsync<ReceivablesSuggestFifoApplyResponse>(Json);
            suggest.Should().NotBeNull();
            suggest!.SuggestedApplies.Should().HaveCount(1);

            var s = suggest.SuggestedApplies.Single();
            s.ApplyId.Should().NotBeNull();

            var applyId = s.ApplyId!.Value;

            // Relationships are created on draft.
            using var graphHttp = await client.GetAsync($"/api/documents/{PropertyManagementCodes.ReceivableApply}/{applyId}/graph?depth=1&maxNodes=50");
            graphHttp.StatusCode.Should().Be(HttpStatusCode.OK);

            var graph = await graphHttp.Content.ReadFromJsonAsync<RelationshipGraphDto>(Json);
            graph.Should().NotBeNull();

            graph!.Edges.Should().Contain(e =>
                e.FromNodeId == NodeId(PropertyManagementCodes.ReceivableApply, applyId)
                && e.ToNodeId == NodeId(PropertyManagementCodes.ReceivablePayment, payment.Id)
                && e.RelationshipType == "based_on");

            graph.Edges.Should().Contain(e =>
                e.FromNodeId == NodeId(PropertyManagementCodes.ReceivableApply, applyId)
                && e.ToNodeId == NodeId(PropertyManagementCodes.ReceivableCharge, charge.Id)
                && e.RelationshipType == "based_on");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task ApplyBatch_WhenPostingDraftApply_PreservesRelationships_ForGraphExplainability()
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

            var charge = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                display = "RC-1",
                lease_id = lease.Id,
                charge_type_id = rentType.Id,
                due_on_utc = "2026-02-05",
                amount = "50.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                display = "RP-1",
                lease_id = lease.Id,
                received_on_utc = "2026-02-10",
                amount = "30.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var suggestReq = new ReceivablesSuggestFifoApplyRequest(LeaseId: lease.Id, CreateDrafts: true, Limit: 10);
            using var suggestHttp = await client.PostAsJsonAsync("/api/receivables/apply/fifo/suggest/lease", suggestReq);
            suggestHttp.EnsureSuccessStatusCode();

            var suggest = await suggestHttp.Content.ReadFromJsonAsync<ReceivablesSuggestFifoApplyResponse>(Json);
            suggest.Should().NotBeNull();
            suggest!.SuggestedApplies.Should().HaveCount(1);

            var s = suggest.SuggestedApplies.Single();
            var applyId = s.ApplyId!.Value;

            // Post via batch.
            var batchReq = new ReceivablesApplyBatchRequest(
                Applies: [new ReceivablesApplyBatchItem(applyId, s.ApplyPayload)]);

            using var batchHttp = await client.PostAsJsonAsync("/api/receivables/apply/batch", batchReq);
            batchHttp.EnsureSuccessStatusCode();

            // Graph must still show those relationships after posting.
            using var graphHttp = await client.GetAsync($"/api/documents/{PropertyManagementCodes.ReceivableApply}/{applyId}/graph?depth=1&maxNodes=50");
            graphHttp.EnsureSuccessStatusCode();

            var graph = await graphHttp.Content.ReadFromJsonAsync<RelationshipGraphDto>(Json);
            graph.Should().NotBeNull();

            graph!.Edges.Should().Contain(e =>
                e.FromNodeId == NodeId(PropertyManagementCodes.ReceivableApply, applyId)
                && e.ToNodeId == NodeId(PropertyManagementCodes.ReceivablePayment, payment.Id)
                && e.RelationshipType == "based_on");

            graph.Edges.Should().Contain(e =>
                e.FromNodeId == NodeId(PropertyManagementCodes.ReceivableApply, applyId)
                && e.ToNodeId == NodeId(PropertyManagementCodes.ReceivableCharge, charge.Id)
                && e.RelationshipType == "based_on");
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

    private static string NodeId(string typeCode, Guid id) => $"doc:{typeCode}:{id}";

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static async Task DisposeFactoryAsync(PmApiFactory factory)
    {
        try { await factory.DisposeAsync(); }
        catch { /* ignore */ }
    }
}
