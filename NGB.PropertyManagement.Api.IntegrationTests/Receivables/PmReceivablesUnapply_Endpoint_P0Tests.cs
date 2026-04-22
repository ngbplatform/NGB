using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Runtime;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Receivables;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReceivablesUnapply_Endpoint_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmReceivablesUnapply_Endpoint_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Unapply_UnpostsApply_AndRestoresOpenItems()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        await using var scope = factory.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var ctx = await CreateLeaseChargeAndPaymentAsync(setup, catalogs, documents, suffix: "unapply", unitNo: "301", chargeAmount: 100m, paymentAmount: 120m);
        var applyId = await ExecuteCustomApplyAsync(client, ctx.Payment.Id, ctx.Charge.Id, 70m);

        using var http = await client.PostAsync($"/api/receivables/apply/{applyId}/unapply", content: null);
        http.StatusCode.Should().Be(HttpStatusCode.OK);

        var resp = await http.Content.ReadFromJsonAsync<ReceivablesUnapplyResponse>();
        resp.Should().NotBeNull();
        resp!.ApplyId.Should().Be(applyId);
        resp.CreditDocumentId.Should().Be(ctx.Payment.Id);
        resp.ChargeDocumentId.Should().Be(ctx.Charge.Id);
        resp.AppliedOnUtc.Should().Be(new DateOnly(2026, 2, 7));
        resp.UnappliedAmount.Should().Be(70m);

        var apply = await documents.GetByIdAsync(PropertyManagementCodes.ReceivableApply, applyId, CancellationToken.None);
        apply.Status.Should().Be(DocumentStatus.Draft);

        var details = await client.GetFromJsonAsync<ReceivablesOpenItemsDetailsResponse>(
            $"/api/receivables/open-items/details?partyId={ctx.Party.Id}&propertyId={ctx.Property.Id}&leaseId={ctx.Lease.Id}");

        details.Should().NotBeNull();
        details!.TotalOutstanding.Should().Be(100m);
        details.TotalCredit.Should().Be(120m);
        details.Allocations.Should().BeEmpty();
        details.Charges.Should().ContainSingle(x => x.ChargeDocumentId == ctx.Charge.Id && x.OutstandingAmount == 100m);
        details.Credits.Should().ContainSingle(x => x.CreditDocumentId == ctx.Payment.Id && x.AvailableCredit == 120m);
    }

    [Fact]
    public async Task Unapply_WhenRepeated_ReturnsFriendlyConflict_AndDoesNotChangeStateAgain()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        await using var scope = factory.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var ctx = await CreateLeaseChargeAndPaymentAsync(setup, catalogs, documents, suffix: "repeat", unitNo: "302", chargeAmount: 80m, paymentAmount: 80m);
        var applyId = await ExecuteCustomApplyAsync(client, ctx.Payment.Id, ctx.Charge.Id, 80m);

        (await client.PostAsync($"/api/receivables/apply/{applyId}/unapply", content: null)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var second = await client.PostAsync($"/api/receivables/apply/{applyId}/unapply", content: null);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var root = await ReadJsonAsync(second);
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("doc.workflow.state_mismatch");
        root.GetProperty("detail").GetString().Should().Be("Expected Posted state, got Draft.");
        root.GetProperty("error").GetProperty("context").GetProperty("operation").GetString().Should().Be("Document.Unpost");
        root.GetProperty("error").GetProperty("context").GetProperty("actualState").GetString().Should().Be("Draft");

        var apply = await documents.GetByIdAsync(PropertyManagementCodes.ReceivableApply, applyId, CancellationToken.None);
        apply.Status.Should().Be(DocumentStatus.Draft);

        var details = await client.GetFromJsonAsync<ReceivablesOpenItemsDetailsResponse>(
            $"/api/receivables/open-items/details?partyId={ctx.Party.Id}&propertyId={ctx.Property.Id}&leaseId={ctx.Lease.Id}");

        details.Should().NotBeNull();
        details!.Allocations.Should().BeEmpty();
        details.TotalOutstanding.Should().Be(80m);
        details.TotalCredit.Should().Be(80m);
    }

    private static async Task<Guid> ExecuteCustomApplyAsync(HttpClient client, Guid creditDocumentId, Guid chargeDocumentId, decimal amount)
    {
        using var http = await client.PostAsJsonAsync(
            "/api/receivables/apply/custom/execute",
            new ReceivablesCustomApplyExecuteRequest(creditDocumentId, [new ReceivablesCustomApplyLine(chargeDocumentId, amount)]));
        http.EnsureSuccessStatusCode();

        var resp = await http.Content.ReadFromJsonAsync<ReceivablesCustomApplyExecuteResponse>();
        resp.Should().NotBeNull();
        resp!.ExecutedApplies.Should().ContainSingle();
        return resp.ExecutedApplies[0].ApplyId;
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
            amount = chargeAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
        }), CancellationToken.None);
        (await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

        var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
        {
            lease_id = lease.Id,
            received_on_utc = "2026-02-07",
            amount = paymentAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
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

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage resp)
    {
        var text = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private sealed record TestContext(CatalogItemDto Party, CatalogItemDto Property, DocumentDto Lease, DocumentDto Charge, DocumentDto Payment);
}
