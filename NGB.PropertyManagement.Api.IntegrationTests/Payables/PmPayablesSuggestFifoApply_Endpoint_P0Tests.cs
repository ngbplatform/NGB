using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Contracts.Payables;
using NGB.PropertyManagement.Runtime;
using Npgsql;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Payables;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmPayablesSuggestFifoApply_Endpoint_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmPayablesSuggestFifoApply_Endpoint_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SuggestFifoApply_WhenCreditMemoExists_ReturnsGeneralizedCreditSourceAndNoDraftsByDefault()
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
                display = "Vendor B",
                is_vendor = true,
                is_tenant = false,
            }), CancellationToken.None);

            var property = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
            {
                kind = "Building",
                address_line1 = "9 Demo Way",
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
                vendor_invoice_no = "INV-200",
            }), CancellationToken.None);
            charge = await documents.PostAsync(PropertyManagementCodes.PayableCharge, charge.Id, CancellationToken.None);

            var creditMemo = await documents.CreateDraftAsync(PropertyManagementCodes.PayableCreditMemo, Payload(new
            {
                party_id = vendor.Id,
                property_id = property.Id,
                charge_type_id = repairType.Id,
                credited_on_utc = "2026-03-07",
                amount = "60.00",
                memo = "Memo credit",
            }), CancellationToken.None);
            creditMemo = await documents.PostAsync(PropertyManagementCodes.PayableCreditMemo, creditMemo.Id, CancellationToken.None);

            var req = new PayablesSuggestFifoApplyRequest(
                PartyId: vendor.Id,
                PropertyId: property.Id,
                AsOfMonth: null,
                ToMonth: null,
                Limit: null,
                CreateDrafts: false);

            var http = await client.PostAsJsonAsync("/api/payables/apply/fifo/suggest", req);
            http.EnsureSuccessStatusCode();

            var resp = await http.Content.ReadFromJsonAsync<PayablesSuggestFifoApplyResponse>();
            resp.Should().NotBeNull();

            resp!.VendorId.Should().Be(vendor.Id);
            resp.PropertyId.Should().Be(property.Id);
            resp.TotalOutstanding.Should().Be(100m);
            resp.TotalCredit.Should().Be(60m);
            resp.TotalApplied.Should().Be(60m);
            resp.RemainingOutstanding.Should().Be(40m);
            resp.RemainingCredit.Should().Be(0m);

            resp.SuggestedApplies.Should().ContainSingle();
            resp.SuggestedApplies[0].CreditDocumentId.Should().Be(creditMemo.Id);
            resp.SuggestedApplies[0].CreditDocumentType.Should().Be(PropertyManagementCodes.PayableCreditMemo);
            resp.SuggestedApplies[0].ChargeDocumentId.Should().Be(charge.Id);
            resp.SuggestedApplies[0].CreditDocumentDateUtc.Should().Be(new DateOnly(2026, 3, 7));
            resp.SuggestedApplies[0].Amount.Should().Be(60m);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();
            (await conn.ExecuteScalarAsync<int>("select count(*) from doc_pm_payable_apply;")).Should().Be(0);
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
