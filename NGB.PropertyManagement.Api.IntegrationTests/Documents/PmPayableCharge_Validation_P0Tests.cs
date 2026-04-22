using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Runtime;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmPayableCharge_Validation_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmPayableCharge_Validation_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_WhenPartyIsNotVendor_Returns400_WithFriendlyFieldError()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();

            await setup.EnsureDefaultsAsync(CancellationToken.None);

            var tenantOnlyParty = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new
            {
                display = "Tenant Only",
                is_tenant = true,
                is_vendor = false
            }), CancellationToken.None);

            var property = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
            {
                kind = "Building",
                display = "Main Building",
                address_line1 = "1 Demo Way",
                city = "Hoboken",
                state = "NJ",
                zip = "07030"
            }), CancellationToken.None);

            var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.PayableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
            var repairType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Repair", StringComparison.OrdinalIgnoreCase));

            using var resp = await client.PostAsJsonAsync(
                $"/api/documents/{PropertyManagementCodes.PayableCharge}",
                new
                {
                    fields = new
                    {
                        party_id = tenantOnlyParty.Id,
                        property_id = property.Id,
                        charge_type_id = repairType.Id,
                        due_on_utc = "2026-03-05",
                        amount = 125.00m,
                        vendor_invoice_no = "INV-100",
                        memo = "Repair bill"
                    }
                });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.payable_charge.vendor_required");
            root.GetProperty("detail").GetString().Should().Be("Selected party must have Vendor role enabled.");
            root.GetProperty("error").GetProperty("errors").GetProperty("party_id").EnumerateArray().Select(x => x.GetString())
                .Should().Contain("Selected party must have Vendor role enabled.");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    private static RecordPayload Payload(object fields)
    {
        var el = JsonSerializer.SerializeToElement(fields);
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
