using System.Net;
using System.Text.Json;
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
public sealed class PmReceivableCharge_PostValidation_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmReceivableCharge_PostValidation_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Post_WhenLeaseWasMarkedForDeletionAfterDraft_Returns400_WithFriendlyFieldError()
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

            var (party, _, unit, lease, rentType) = await CreateLeaseAndRentChargeTypeAsync(catalogs, documents);
            var charge = await documents.CreateDraftAsync(
                PropertyManagementCodes.ReceivableCharge,
                Payload(new
                {
                    lease_id = lease.Id,
                    charge_type_id = rentType.Id,
                    due_on_utc = "2026-02-05",
                    amount = "123.45",
                    memo = "Rent receivable"
                }),
                CancellationToken.None);

            var marked = await documents.MarkForDeletionAsync(PropertyManagementCodes.Lease, lease.Id, CancellationToken.None);
            marked.Status.Should().Be(DocumentStatus.MarkedForDeletion);

            using var resp = await client.PostAsync($"/api/documents/{PropertyManagementCodes.ReceivableCharge}/{charge.Id}/post", content: null);

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.receivables.lease_deleted");
            root.GetProperty("detail").GetString().Should().Be("Selected lease is marked for deletion.");
            root.GetProperty("error").GetProperty("errors").GetProperty("lease_id").EnumerateArray().Select(x => x.GetString())
                .Should().Contain("Selected lease is marked for deletion.");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task Post_WhenChargeTypeWasMarkedForDeletionAfterDraft_Returns400_WithFriendlyFieldError()
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

            var (party, _, unit, lease, rentType) = await CreateLeaseAndRentChargeTypeAsync(catalogs, documents);
            var charge = await documents.CreateDraftAsync(
                PropertyManagementCodes.ReceivableCharge,
                Payload(new
                {
                    lease_id = lease.Id,
                    charge_type_id = rentType.Id,
                    due_on_utc = "2026-02-05",
                    amount = "123.45",
                    memo = "Rent receivable"
                }),
                CancellationToken.None);

            await catalogs.MarkForDeletionAsync(PropertyManagementCodes.ReceivableChargeType, rentType.Id, CancellationToken.None);

            using var resp = await client.PostAsync($"/api/documents/{PropertyManagementCodes.ReceivableCharge}/{charge.Id}/post", content: null);

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.receivable_charge.charge_type_deleted");
            root.GetProperty("detail").GetString().Should().Be("Selected charge type is marked for deletion.");
            root.GetProperty("error").GetProperty("errors").GetProperty("charge_type_id").EnumerateArray().Select(x => x.GetString())
                .Should().Contain("Selected charge type is marked for deletion.");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    private static async Task<(CatalogItemDto Party, CatalogItemDto Building, CatalogItemDto Unit, DocumentDto Lease, CatalogItemDto RentType)> CreateLeaseAndRentChargeTypeAsync(
        ICatalogService catalogs,
        IDocumentService documents)
    {
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
            end_on_utc = "2026-02-28",
            rent_amount = "1000.00"
        }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);

        var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
        var rentType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));
        return (party, building, unit, lease, rentType);
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
