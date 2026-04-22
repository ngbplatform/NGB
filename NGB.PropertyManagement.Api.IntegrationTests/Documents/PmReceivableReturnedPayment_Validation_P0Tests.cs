using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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
public sealed class PmReceivableReturnedPayment_Validation_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmReceivableReturnedPayment_Validation_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateDraft_WhenOriginalPaymentIsNotPosted_AllowsDraftCreation()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var (party, property, lease, payment) = await SeedDraftPaymentAsync(scope.ServiceProvider);

            using var resp = await client.PostAsJsonAsync(
                $"/api/documents/{PropertyManagementCodes.ReceivableReturnedPayment}",
                new
                {
                    fields = new
                    {
                        original_payment_id = payment.Id,
                        returned_on_utc = "2026-02-08",
                        amount = "50.00"
                    }
                });

            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var created = await resp.Content.ReadFromJsonAsync<NGB.Contracts.Services.DocumentDto>(JsonOptions);
            created.Should().NotBeNull();
            created!.Status.Should().Be(NGB.Contracts.Metadata.DocumentStatus.Draft);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task CreateDraft_WhenReturnedOnIsEarlierThanOriginalPayment_Returns400()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var (party, property, lease, payment) = await SeedDraftPaymentAsync(scope.ServiceProvider);
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
            (await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(NGB.Contracts.Metadata.DocumentStatus.Posted);

            using var resp = await client.PostAsJsonAsync(
                $"/api/documents/{PropertyManagementCodes.ReceivableReturnedPayment}",
                new
                {
                    fields = new
                    {
                        original_payment_id = payment.Id,
                        returned_on_utc = "2026-02-06",
                        amount = "50.00"
                    }
                });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.receivable_returned_payment.returned_on_before_original_payment");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    private static async Task<(NGB.Contracts.Services.CatalogItemDto Party, NGB.Contracts.Services.CatalogItemDto Property, NGB.Contracts.Services.DocumentDto Lease, NGB.Contracts.Services.DocumentDto Payment)> SeedDraftPaymentAsync(IServiceProvider services)
    {
        var setup = services.GetRequiredService<IPropertyManagementSetupService>();
        var catalogs = services.GetRequiredService<ICatalogService>();
        var documents = services.GetRequiredService<IDocumentService>();

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

        var lease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
        {
            property_id = property.Id,
            start_on_utc = "2026-02-01",
            end_on_utc = "2026-02-28",
            rent_amount = "1000.00"
        }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);

        var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
        {
            lease_id = lease.Id,
            received_on_utc = "2026-02-07",
            amount = "100.00"
        }), CancellationToken.None);

        return (party, property, lease, payment);
    }


    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

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
        catch { }
    }
}
