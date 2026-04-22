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
public sealed class PmReceivableCreditMemo_Validation_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmReceivableCreditMemo_Validation_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateDraft_WithLeaseAndChargeType_AllowsDraftCreation()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var seeded = await SeedLeaseAndChargeTypeAsync(scope.ServiceProvider);

            using var resp = await client.PostAsJsonAsync($"/api/documents/{PropertyManagementCodes.ReceivableCreditMemo}", new
            {
                fields = new
                {
                    lease_id = seeded.Lease.Id,
                    charge_type_id = seeded.ChargeType.Id,
                    credited_on_utc = "2026-02-08",
                    amount = "50.00"
                }
            });

            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            using var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            json.RootElement.GetProperty("id").GetGuid().Should().NotBe(Guid.Empty);
            var status = json.RootElement.GetProperty("status");
            if (status.ValueKind == JsonValueKind.String)
                status.GetString().Should().Be(nameof(DocumentStatus.Draft));
            else
                status.GetInt32().Should().Be((int)DocumentStatus.Draft);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task CreateDraft_WhenChargeTypeDoesNotExist_Returns400()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var seeded = await SeedLeaseAndChargeTypeAsync(scope.ServiceProvider);

            using var resp = await client.PostAsJsonAsync($"/api/documents/{PropertyManagementCodes.ReceivableCreditMemo}", new
            {
                fields = new
                {
                    lease_id = seeded.Lease.Id,
                    charge_type_id = Guid.CreateVersion7(),
                    credited_on_utc = "2026-02-08",
                    amount = "50.00"
                }
            });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.receivable_credit_memo.charge_type_not_found");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    private static async Task<(CatalogItemDto Party, CatalogItemDto Property, DocumentDto Lease, CatalogItemDto ChargeType)> SeedLeaseAndChargeTypeAsync(IServiceProvider services)
    {
        var setup = services.GetRequiredService<IPropertyManagementSetupService>();
        var catalogs = services.GetRequiredService<ICatalogService>();
        var documents = services.GetRequiredService<IDocumentService>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);
        var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "P" }), CancellationToken.None);
        var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new { kind = "Building", display = "A", address_line1 = "A", city = "Hoboken", state = "NJ", zip = "07030" }), CancellationToken.None);
        var property = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new { kind = "Unit", parent_property_id = building.Id, unit_no = "101" }), CancellationToken.None);
        var lease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new { property_id = property.Id, start_on_utc = "2026-02-01", end_on_utc = "2026-02-28", rent_amount = "1000.00" }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);
        var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
        var chargeType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));
        return (party, property, lease, chargeType);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static JsonSerializerOptions CreateJson() => JsonOptions;

    private static RecordPayload Payload(object obj, IReadOnlyDictionary<string, RecordPartPayload>? parts = null)
    {
        var el = JsonSerializer.SerializeToElement(obj);
        var dict = new Dictionary<string, JsonElement>();
        foreach (var p in el.EnumerateObject()) dict[p.Name] = p.Value;
        return new RecordPayload(dict, parts);
    }

    private static async Task DisposeFactoryAsync(PmApiFactory factory)
    {
        try { await factory.DisposeAsync(); } catch { }
    }
}
