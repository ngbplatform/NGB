using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Contracts.Payables;
using NGB.PropertyManagement.Runtime;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Payables;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmPayablesOpenItems_DetailsEndpoint_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmPayablesOpenItems_DetailsEndpoint_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetOpenItemsDetails_WhenCreditMemoApplied_ReturnsGeneralizedCreditSourceShape()
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

            var vendor = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new
            {
                display = "Vendor A",
                is_vendor = true,
                is_tenant = false,
            }), CancellationToken.None);

            var property = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
            {
                kind = "Building",
                address_line1 = "1 Demo Way",
                city = "Hoboken",
                state = "NJ",
                zip = "07030",
            }), CancellationToken.None);

            var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.PayableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
            var repairType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Repair", StringComparison.OrdinalIgnoreCase));

            var charge = await documents.CreateDraftAsync(PropertyManagementCodes.PayableCharge, Payload(new
            {
                party_id = vendor.Id,
                property_id = property.Id,
                charge_type_id = repairType.Id,
                due_on_utc = "2026-03-05",
                amount = "100.00",
                vendor_invoice_no = "INV-100",
                memo = "Charge",
            }), CancellationToken.None);
            charge = await documents.PostAsync(PropertyManagementCodes.PayableCharge, charge.Id, CancellationToken.None);

            var creditMemo = await documents.CreateDraftAsync(PropertyManagementCodes.PayableCreditMemo, Payload(new
            {
                party_id = vendor.Id,
                property_id = property.Id,
                charge_type_id = repairType.Id,
                credited_on_utc = "2026-03-07",
                amount = "60.00",
                memo = "Vendor credit",
            }), CancellationToken.None);
            creditMemo = await documents.PostAsync(PropertyManagementCodes.PayableCreditMemo, creditMemo.Id, CancellationToken.None);

            var apply = await documents.CreateDraftAsync(PropertyManagementCodes.PayableApply, Payload(new
            {
                credit_document_id = creditMemo.Id,
                charge_document_id = charge.Id,
                applied_on_utc = "2026-03-07",
                amount = "40.00",
            }), CancellationToken.None);
            apply = await documents.PostAsync(PropertyManagementCodes.PayableApply, apply.Id, CancellationToken.None);

            var url = $"/api/payables/open-items/details?partyId={vendor.Id}&propertyId={property.Id}";
            var resp = await client.GetFromJsonAsync<PayablesOpenItemsDetailsResponse>(url);
            resp.Should().NotBeNull();

            resp!.Charges.Should().ContainSingle(x =>
                x.ChargeDocumentId == charge.Id
                && x.DocumentType == PropertyManagementCodes.PayableCharge
                && x.DueOnUtc == new DateOnly(2026, 3, 5)
                && x.OutstandingAmount == 60m
                && x.OriginalAmount == 100m
                && string.Equals(x.ChargeTypeDisplay, "Repair", StringComparison.OrdinalIgnoreCase));

            resp.Credits.Should().ContainSingle(x =>
                x.CreditDocumentId == creditMemo.Id
                && x.DocumentType == PropertyManagementCodes.PayableCreditMemo
                && x.CreditDocumentDateUtc == new DateOnly(2026, 3, 7)
                && x.AvailableCredit == 20m
                && x.OriginalAmount == 60m);

            resp.Allocations.Should().ContainSingle(x =>
                x.ApplyId == apply.Id
                && x.CreditDocumentId == creditMemo.Id
                && x.CreditDocumentType == PropertyManagementCodes.PayableCreditMemo
                && x.ChargeDocumentId == charge.Id
                && x.ChargeDocumentType == PropertyManagementCodes.PayableCharge
                && x.AppliedOnUtc == new DateOnly(2026, 3, 7)
                && x.Amount == 40m
                && x.IsPosted);

            resp.Allocations[0].ApplyDisplay.Should().NotBeNullOrWhiteSpace();
            resp.Allocations[0].ApplyNumber.Should().NotBeNullOrWhiteSpace();
            resp.Allocations[0].CreditDocumentDisplay.Should().NotBeNullOrWhiteSpace();
            resp.Allocations[0].ChargeDisplay.Should().NotBeNullOrWhiteSpace();
            resp.Allocations[0].CreditDocumentNumber.Should().NotBeNullOrWhiteSpace();
            resp.Allocations[0].ChargeNumber.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    private static RecordPayload Payload(object obj)
    {
        var el = JsonSerializer.SerializeToElement(obj);
        var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in el.EnumerateObject())
            dict[p.Name] = p.Value.Clone();
        return new RecordPayload(dict, null);
    }

    private static async Task DisposeFactoryAsync(PmApiFactory factory)
    {
        await factory.DisposeAsync();
        factory.Dispose();
    }
}
