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
using NGB.PropertyManagement.Contracts.Payables;
using NGB.PropertyManagement.Runtime;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Payables;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmPayablesUnapply_Endpoint_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmPayablesUnapply_Endpoint_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

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

        var ctx = await CreateChargeAndCreditMemoAsync(setup, catalogs, documents, suffix: "unapply", amount: 100m, creditAmount: 80m);
        var applyId = await ExecuteApplyBatchAsync(client, ctx.CreditMemo.Id, ctx.Charge.Id, 70m);

        using var http = await client.PostAsync($"/api/payables/apply/{applyId}/unapply", content: null);
        http.StatusCode.Should().Be(HttpStatusCode.OK);

        var resp = await http.Content.ReadFromJsonAsync<PayablesUnapplyResponse>();
        resp.Should().NotBeNull();
        resp!.ApplyId.Should().Be(applyId);
        resp.CreditDocumentId.Should().Be(ctx.CreditMemo.Id);
        resp.ChargeDocumentId.Should().Be(ctx.Charge.Id);
        resp.AppliedOnUtc.Should().Be(new DateOnly(2026, 3, 8));
        resp.UnappliedAmount.Should().Be(70m);

        var apply = await documents.GetByIdAsync(PropertyManagementCodes.PayableApply, applyId, CancellationToken.None);
        apply.Status.Should().Be(DocumentStatus.Draft);

        var details = await client.GetFromJsonAsync<PayablesOpenItemsDetailsResponse>(
            $"/api/payables/open-items/details?partyId={ctx.Vendor.Id}&propertyId={ctx.Property.Id}");

        details.Should().NotBeNull();
        details!.TotalOutstanding.Should().Be(100m);
        details.TotalCredit.Should().Be(80m);
        details.Allocations.Should().BeEmpty();
        details.Charges.Should().ContainSingle(x => x.ChargeDocumentId == ctx.Charge.Id && x.OutstandingAmount == 100m);
        details.Credits.Should().ContainSingle(x => x.CreditDocumentId == ctx.CreditMemo.Id && x.AvailableCredit == 80m);
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

        var ctx = await CreateChargeAndCreditMemoAsync(setup, catalogs, documents, suffix: "repeat", amount: 80m, creditAmount: 80m);
        var applyId = await ExecuteApplyBatchAsync(client, ctx.CreditMemo.Id, ctx.Charge.Id, 80m);

        (await client.PostAsync($"/api/payables/apply/{applyId}/unapply", content: null)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var second = await client.PostAsync($"/api/payables/apply/{applyId}/unapply", content: null);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var root = await ReadJsonAsync(second);
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("doc.workflow.state_mismatch");
        root.GetProperty("detail").GetString().Should().Be("Expected Posted state, got Draft.");
        root.GetProperty("error").GetProperty("context").GetProperty("operation").GetString().Should().Be("Document.Unpost");
        root.GetProperty("error").GetProperty("context").GetProperty("actualState").GetString().Should().Be("Draft");

        var apply = await documents.GetByIdAsync(PropertyManagementCodes.PayableApply, applyId, CancellationToken.None);
        apply.Status.Should().Be(DocumentStatus.Draft);

        var details = await client.GetFromJsonAsync<PayablesOpenItemsDetailsResponse>(
            $"/api/payables/open-items/details?partyId={ctx.Vendor.Id}&propertyId={ctx.Property.Id}");

        details.Should().NotBeNull();
        details!.Allocations.Should().BeEmpty();
        details.TotalOutstanding.Should().Be(80m);
        details.TotalCredit.Should().Be(80m);
    }

    private static async Task<Guid> ExecuteApplyBatchAsync(HttpClient client, Guid creditDocumentId, Guid chargeDocumentId, decimal amount)
    {
        using var http = await client.PostAsJsonAsync(
            "/api/payables/apply/batch",
            new PayablesApplyBatchRequest([
                new PayablesApplyBatchItem(null, Payload(new
                {
                    credit_document_id = creditDocumentId,
                    charge_document_id = chargeDocumentId,
                    applied_on_utc = "2026-03-08",
                    amount = amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                }))
            ]));
        http.EnsureSuccessStatusCode();

        var resp = await http.Content.ReadFromJsonAsync<PayablesApplyBatchResponse>();
        resp.Should().NotBeNull();
        resp!.ExecutedApplies.Should().ContainSingle();
        return resp.ExecutedApplies[0].ApplyId;
    }

    private static async Task<TestContext> CreateChargeAndCreditMemoAsync(
        IPropertyManagementSetupService setup,
        ICatalogService catalogs,
        IDocumentService documents,
        string suffix,
        decimal amount,
        decimal creditAmount)
    {
        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var vendor = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new
        {
            display = $"Vendor {suffix}",
            is_vendor = true,
            is_tenant = false
        }), CancellationToken.None);

        var property = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            address_line1 = $"{suffix} Main St",
            city = "Hoboken",
            state = "NJ",
            zip = "07030"
        }), CancellationToken.None);

        var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.PayableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
        var repairType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Repair", StringComparison.OrdinalIgnoreCase));

        var charge = await documents.CreateDraftAsync(PropertyManagementCodes.PayableCharge, Payload(new
        {
            party_id = vendor.Id,
            property_id = property.Id,
            charge_type_id = repairType.Id,
            due_on_utc = "2026-03-05",
            amount = amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            vendor_invoice_no = $"INV-{suffix}"
        }), CancellationToken.None);
        (await documents.PostAsync(PropertyManagementCodes.PayableCharge, charge.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

        var creditMemo = await documents.CreateDraftAsync(PropertyManagementCodes.PayableCreditMemo, Payload(new
        {
            party_id = vendor.Id,
            property_id = property.Id,
            charge_type_id = repairType.Id,
            credited_on_utc = "2026-03-07",
            amount = creditAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
        }), CancellationToken.None);
        (await documents.PostAsync(PropertyManagementCodes.PayableCreditMemo, creditMemo.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

        return new TestContext(vendor, property, charge, creditMemo);
    }

    private static RecordPayload Payload(object obj)
    {
        var el = JsonSerializer.SerializeToElement(obj);
        var dict = new Dictionary<string, JsonElement>();
        foreach (var p in el.EnumerateObject())
            dict[p.Name] = p.Value;
        return new RecordPayload(dict, null);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage resp)
    {
        var text = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private sealed record TestContext(CatalogItemDto Vendor, CatalogItemDto Property, DocumentDto Charge, DocumentDto CreditMemo);
}
