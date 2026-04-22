using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Runtime;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Admin;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReceivablesOpenItems_AdminEndpoint_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmReceivablesOpenItems_AdminEndpoint_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetOpenItems_ReturnsOutstandingChargesAndCredits()
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

            var lease = await documents.CreateDraftAsync(
                PropertyManagementCodes.Lease,
                Payload(
                    new
                    {
                        property_id = property.Id,
                        start_on_utc = "2026-02-01",
                        rent_amount = "1000.00"
                    },
                    LeaseParts.PrimaryTenant(party.Id)),
                CancellationToken.None);

            var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
            var rentType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));

            var charge = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                display = "RC-1",
                lease_id = lease.Id,
                charge_type_id = rentType.Id,
                due_on_utc = "2026-02-05",
                amount = "100.00",
                memo = "Charge memo"
            }), CancellationToken.None);

            (await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                display = "RP-1",
                lease_id = lease.Id,
                received_on_utc = "2026-02-07",
                amount = "120.00",
                memo = "Payment memo"
            }), CancellationToken.None);

            (await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var apply = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableApply, Payload(new
            {
                display = "RA-1",
                credit_document_id = payment.Id,
                charge_document_id = charge.Id,
                applied_on_utc = "2026-02-07",
                amount = "70.00",
            }), CancellationToken.None);

            (await documents.PostAsync(PropertyManagementCodes.ReceivableApply, apply.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            // PM-UI-01: leaseId is required; partyId/propertyId are optional.
            var url = $"/api/receivables/open-items?leaseId={lease.Id}";
            var resp = await client.GetFromJsonAsync<ReceivablesOpenItemsDetailsResponse>(url);
            resp.Should().NotBeNull();

            resp!.LeaseId.Should().Be(lease.Id);
	            resp.LeaseDisplay.Should().Be("A #101 — 02/01/2026 → Open");
            resp.PartyId.Should().Be(party.Id);
            resp.PartyDisplay.Should().Be("P");
            resp.PropertyId.Should().Be(property.Id);
	            resp.PropertyDisplay.Should().Be("A #101");

            resp.Charges.Should().ContainSingle(x =>
                x.ChargeDocumentId == charge.Id &&
                x.OutstandingAmount == 30m &&
                x.OriginalAmount == 100m &&
                x.Memo == "Charge memo");

            resp.Credits.Should().ContainSingle(x =>
                x.CreditDocumentId == payment.Id &&
                x.AvailableCredit == 50m &&
                x.OriginalAmount == 120m &&
                x.Memo == "Payment memo");

            resp.TotalOutstanding.Should().Be(30m);
            resp.TotalCredit.Should().Be(50m);
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
