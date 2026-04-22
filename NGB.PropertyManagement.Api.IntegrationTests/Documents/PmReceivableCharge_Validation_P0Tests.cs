using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Runtime;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReceivableCharge_Validation_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmReceivableCharge_Validation_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Metadata_Includes_Amount_In_ListColumns()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var metaResp = await client.GetAsync($"/api/documents/{PropertyManagementCodes.ReceivableCharge}/metadata");
        metaResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var meta = await metaResp.Content.ReadFromJsonAsync<DocumentTypeMetadataDto>(Json);
        meta.Should().NotBeNull();
        meta!.DocumentType.Should().Be(PropertyManagementCodes.ReceivableCharge);
        meta.List!.Columns.Should().Contain(c => c.Key == "amount" && c.Label == "Amount");
    }

    [Fact]
    public async Task Create_WhenAmountIsNotPositive_Returns400_WithFriendlyFieldError()
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
            var (_, _, _, lease, rentType) = await CreateLeaseAndChargeTypeAsync(catalogs, documents);

            using var resp = await client.PostAsJsonAsync($"/api/documents/{PropertyManagementCodes.ReceivableCharge}", new
            {
                fields = new
                {
                    lease_id = lease.Id,
                    charge_type_id = rentType.Id,
                    due_on_utc = "2026-02-05",
                    amount = 0m,
                    memo = "Zero charge"
                }
            });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.receivable_charge.amount_must_be_positive");
            root.GetProperty("error").GetProperty("errors").GetProperty("amount").EnumerateArray().Select(x => x.GetString()).Should().Contain("Amount must be positive.");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task Metadata_DoesNotExpose_Party_Or_Property_Fields()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var metaResp = await client.GetAsync($"/api/documents/{PropertyManagementCodes.ReceivableCharge}/metadata");
        metaResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var meta = await metaResp.Content.ReadFromJsonAsync<DocumentTypeMetadataDto>(Json);
        var fields = meta!.Form!.Sections.SelectMany(s => s.Rows).SelectMany(r => r.Fields).ToList();
        fields.Should().NotContain(f => f.Key == "party_id");
        fields.Should().NotContain(f => f.Key == "property_id");
    }

    private static async Task<(CatalogItemDto Party, CatalogItemDto Building, CatalogItemDto Unit, DocumentDto Lease, CatalogItemDto RentType)> CreateLeaseAndChargeTypeAsync(ICatalogService catalogs, IDocumentService documents)
    {
        var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "Tenant A" }), CancellationToken.None);
        var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            display = "A",
            address_line1 = "A",
            city = "Hoboken",
            state = "NJ",
            zip = "07030"
        }), CancellationToken.None);
        var unit = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new { kind = "Unit", parent_property_id = building.Id, unit_no = "101" }), CancellationToken.None);
        var lease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new { property_id = unit.Id, start_on_utc = "2026-02-01", rent_amount = "1000.00" }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);
        var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
        var rentType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));
        return (party, building, unit, lease, rentType);
    }

    private static RecordPayload Payload(object obj, IReadOnlyDictionary<string, RecordPartPayload>? parts = null)
    {
        var el = JsonSerializer.SerializeToElement(obj);
        var dict = new Dictionary<string, JsonElement>();
        foreach (var p in el.EnumerateObject()) dict[p.Name] = p.Value;
        return new RecordPayload(dict, parts);
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static async Task DisposeFactoryAsync(PmApiFactory factory)
    {
        try { await factory.DisposeAsync(); } catch { }
    }
}
