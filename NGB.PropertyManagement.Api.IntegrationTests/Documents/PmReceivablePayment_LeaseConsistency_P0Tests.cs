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
public sealed class PmReceivablePayment_LeaseConsistency_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmReceivablePayment_LeaseConsistency_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Metadata_Uses_LeaseOnly_Context_Fields()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var resp = await client.GetAsync($"/api/documents/{PropertyManagementCodes.ReceivablePayment}/metadata");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var meta = await resp.Content.ReadFromJsonAsync<DocumentTypeMetadataDto>(Json);
        meta.Should().NotBeNull();

        var fields = meta!.Form!.Sections.SelectMany(s => s.Rows).SelectMany(r => r.Fields).ToList();
        fields.Should().Contain(f => f.Key == "lease_id");
        fields.Should().NotContain(f => f.Key == "party_id");
        fields.Should().NotContain(f => f.Key == "property_id");
    }

    [Fact]
    public async Task CreateDraft_WithLeaseOnlyContext_Succeeds()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            await setup.EnsureDefaultsAsync(CancellationToken.None);
            var (_, _, _, lease) = await CreateLeaseAsync(catalogs, documents);

            var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                lease_id = lease.Id,
                received_on_utc = "2026-02-07",
                amount = "50.00",
                memo = "Lease-based payment"
            }), CancellationToken.None);

            payment.Status.Should().Be(DocumentStatus.Draft);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    private static async Task<(CatalogItemDto Party, CatalogItemDto Building, CatalogItemDto Unit, DocumentDto Lease)> CreateLeaseAsync(
        ICatalogService catalogs,
        IDocumentService documents)
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

        var unit = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Unit",
            parent_property_id = building.Id,
            unit_no = "101"
        }), CancellationToken.None);

        var lease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
        {
            property_id = unit.Id,
            start_on_utc = "2026-02-01",
            rent_amount = "1000.00"
        }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);

        return (party, building, unit, lease);
    }

    private static RecordPayload Payload(object obj, IReadOnlyDictionary<string, RecordPartPayload>? parts = null)
    {
        var el = JsonSerializer.SerializeToElement(obj);
        var dict = new Dictionary<string, JsonElement>();
        foreach (var p in el.EnumerateObject())
            dict[p.Name] = p.Value;
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
        try { await factory.DisposeAsync(); }
        catch { }
    }
}
