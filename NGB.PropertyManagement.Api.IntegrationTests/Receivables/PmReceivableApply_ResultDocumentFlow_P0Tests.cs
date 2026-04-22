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
using NGB.Contracts.Services;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Runtime;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Receivables;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReceivableApply_ResultDocumentFlow_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmReceivableApply_ResultDocumentFlow_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ExecuteCustomApply_ResultDocument_CanBeOpened_AndGraphExplainsIt()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            var ctx = await CreateLeaseChargeAndPaymentAsync(setup, catalogs, documents, suffix: "custom", unitNo: "101", chargeAmount: 100m, paymentAmount: 120m);

            var req = new ReceivablesCustomApplyExecuteRequest(
                CreditDocumentId: ctx.Payment.Id,
                Applies:
                [
                    new ReceivablesCustomApplyLine(ctx.Charge.Id, 70m)
                ]);

            using var http = await client.PostAsJsonAsync("/api/receivables/apply/custom/execute", req);
            http.StatusCode.Should().Be(HttpStatusCode.OK);

            var resp = await http.Content.ReadFromJsonAsync<ReceivablesCustomApplyExecuteResponse>(Json);
            resp.Should().NotBeNull();
            resp!.ExecutedApplies.Should().ContainSingle();

            var applyId = resp.ExecutedApplies[0].ApplyId;

            var byId = await client.GetFromJsonAsync<DocumentDto>(
                $"/api/documents/{PropertyManagementCodes.ReceivableApply}/{applyId}",
                Json);

            byId.Should().NotBeNull();
            byId!.Status.Should().Be(DocumentStatus.Posted);
            byId.Display.Should().NotBeNullOrWhiteSpace();
            byId.Payload.Fields.Should().NotBeNull();
            byId.Payload.Fields!["amount"].GetDecimal().Should().Be(70m);
            byId.Payload.Fields["applied_on_utc"].GetString().Should().Be("2026-02-07");
            AssertRef(byId.Payload.Fields["credit_document_id"], ctx.Payment.Id);
            AssertRef(byId.Payload.Fields["charge_document_id"], ctx.Charge.Id);

            using var graphHttp = await client.GetAsync($"/api/documents/{PropertyManagementCodes.ReceivableApply}/{applyId}/graph?depth=1&maxNodes=50");
            graphHttp.StatusCode.Should().Be(HttpStatusCode.OK);

            var graph = await graphHttp.Content.ReadFromJsonAsync<RelationshipGraphDto>(Json);
            graph.Should().NotBeNull();
            graph!.Edges.Should().Contain(e =>
                e.FromNodeId == NodeId(PropertyManagementCodes.ReceivableApply, applyId)
                && e.ToNodeId == NodeId(PropertyManagementCodes.ReceivablePayment, ctx.Payment.Id)
                && e.RelationshipType == "based_on");
            graph.Edges.Should().Contain(e =>
                e.FromNodeId == NodeId(PropertyManagementCodes.ReceivableApply, applyId)
                && e.ToNodeId == NodeId(PropertyManagementCodes.ReceivableCharge, ctx.Charge.Id)
                && e.RelationshipType == "based_on");

            var details = await client.GetFromJsonAsync<ReceivablesOpenItemsDetailsResponse>(
                $"/api/receivables/open-items/details?partyId={ctx.Party.Id}&propertyId={ctx.Property.Id}&leaseId={ctx.Lease.Id}",
                Json);

            details.Should().NotBeNull();
            details!.TotalOutstanding.Should().Be(30m);
            details.TotalCredit.Should().Be(50m);
            details.Charges.Should().ContainSingle(x => x.ChargeDocumentId == ctx.Charge.Id && x.OutstandingAmount == 30m);
            details.Credits.Should().ContainSingle(x => x.CreditDocumentId == ctx.Payment.Id && x.AvailableCredit == 50m);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task ApplyBatch_PostedResultDocument_CanBeOpened_WithRefs_AndGraphStillExplainsIt()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            var ctx = await CreateLeaseChargeAndPaymentAsync(setup, catalogs, documents, suffix: "batch", unitNo: "102", chargeAmount: 75m, paymentAmount: 75m);

            var suggestReq = new ReceivablesSuggestFifoApplyRequest(LeaseId: ctx.Lease.Id, CreateDrafts: true, Limit: 10);
            using var suggestHttp = await client.PostAsJsonAsync("/api/receivables/apply/fifo/suggest/lease", suggestReq);
            suggestHttp.StatusCode.Should().Be(HttpStatusCode.OK);

            var suggest = await suggestHttp.Content.ReadFromJsonAsync<ReceivablesSuggestFifoApplyResponse>(Json);
            suggest.Should().NotBeNull();
            suggest!.SuggestedApplies.Should().ContainSingle();
            suggest.SuggestedApplies[0].ApplyId.Should().NotBeNull();

            var applyId = suggest.SuggestedApplies[0].ApplyId!.Value;
            var batchReq = new ReceivablesApplyBatchRequest(
                Applies: [new ReceivablesApplyBatchItem(applyId, suggest.SuggestedApplies[0].ApplyPayload)]);

            using var batchHttp = await client.PostAsJsonAsync("/api/receivables/apply/batch", batchReq);
            batchHttp.StatusCode.Should().Be(HttpStatusCode.OK);

            var byId = await client.GetFromJsonAsync<DocumentDto>(
                $"/api/documents/{PropertyManagementCodes.ReceivableApply}/{applyId}",
                Json);

            byId.Should().NotBeNull();
            byId!.Status.Should().Be(DocumentStatus.Posted);
            byId.Payload.Fields.Should().NotBeNull();
            byId.Payload.Fields!["amount"].GetDecimal().Should().Be(75m);
            byId.Payload.Fields["applied_on_utc"].GetString().Should().Be("2026-02-07");
            AssertRef(byId.Payload.Fields["credit_document_id"], ctx.Payment.Id);
            AssertRef(byId.Payload.Fields["charge_document_id"], ctx.Charge.Id);

            using var graphHttp = await client.GetAsync($"/api/documents/{PropertyManagementCodes.ReceivableApply}/{applyId}/graph?depth=1&maxNodes=50");
            graphHttp.StatusCode.Should().Be(HttpStatusCode.OK);

            var graph = await graphHttp.Content.ReadFromJsonAsync<RelationshipGraphDto>(Json);
            graph.Should().NotBeNull();
            graph!.Edges.Should().Contain(e =>
                e.FromNodeId == NodeId(PropertyManagementCodes.ReceivableApply, applyId)
                && e.ToNodeId == NodeId(PropertyManagementCodes.ReceivablePayment, ctx.Payment.Id)
                && e.RelationshipType == "based_on");
            graph.Edges.Should().Contain(e =>
                e.FromNodeId == NodeId(PropertyManagementCodes.ReceivableApply, applyId)
                && e.ToNodeId == NodeId(PropertyManagementCodes.ReceivableCharge, ctx.Charge.Id)
                && e.RelationshipType == "based_on");

            var details = await client.GetFromJsonAsync<ReceivablesOpenItemsDetailsResponse>(
                $"/api/receivables/open-items/details?partyId={ctx.Party.Id}&propertyId={ctx.Property.Id}&leaseId={ctx.Lease.Id}",
                Json);

            details.Should().NotBeNull();
            details!.TotalOutstanding.Should().Be(0m);
            details.TotalCredit.Should().Be(0m);
            details.Charges.Should().BeEmpty();
            details.Credits.Should().BeEmpty();
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    private static void AssertRef(JsonElement value, Guid expectedId)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            value.GetGuid().Should().Be(expectedId);
            return;
        }

        value.ValueKind.Should().Be(JsonValueKind.Object);
        value.GetProperty("id").GetGuid().Should().Be(expectedId);
        value.GetProperty("display").GetString().Should().NotBeNullOrWhiteSpace();
    }

    private static async Task<TestContext> CreateLeaseChargeAndPaymentAsync(
        IPropertyManagementSetupService setup,
        ICatalogService catalogs,
        IDocumentService documents,
        string suffix,
        string unitNo,
        decimal chargeAmount,
        decimal paymentAmount)
    {
        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = $"Tenant {suffix}" }), CancellationToken.None);

        var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            display = $"Building {suffix}",
            address_line1 = $"{suffix} Main St",
            city = "Hoboken",
            state = "NJ",
            zip = "07030"
        }), CancellationToken.None);

        var property = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Unit",
            parent_property_id = building.Id,
            unit_no = unitNo
        }), CancellationToken.None);

        var lease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
        {
            property_id = property.Id,
            start_on_utc = "2026-02-01",
            rent_amount = "1000.00"
        }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);

        var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
        var rentType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));

        var charge = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
        {
            lease_id = lease.Id,
            charge_type_id = rentType.Id,
            due_on_utc = "2026-02-05",
            amount = chargeAmount,
        }), CancellationToken.None);
        (await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

        var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
        {
            lease_id = lease.Id,
            received_on_utc = "2026-02-07",
            amount = paymentAmount,
        }), CancellationToken.None);
        (await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

        return new TestContext(party, property, lease, charge, payment);
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

    private sealed record TestContext(CatalogItemDto Party, CatalogItemDto Property, DocumentDto Lease, DocumentDto Charge, DocumentDto Payment);
}
