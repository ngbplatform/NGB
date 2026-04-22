using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Runtime;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReceivableReturnedPayment_PostValidation_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmReceivableReturnedPayment_PostValidation_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PostAsync_WhenOriginalPaymentWasUnpostedAfterDraftCreation_Returns400()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var seeded = await SeedDraftReturnedPaymentAsync(scope.ServiceProvider);
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            (await documents.UnpostAsync(PropertyManagementCodes.ReceivablePayment, seeded.OriginalPayment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Draft);

            using var resp = await client.PostAsync($"/api/documents/{PropertyManagementCodes.ReceivableReturnedPayment}/{seeded.ReturnedPayment.Id}/post", null);
            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.receivable_returned_payment.original_payment_must_be_posted");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task PostAsync_WhenOriginalPaymentWasNeverPosted_Returns400()
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

            var returned = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableReturnedPayment, Payload(new
            {
                original_payment_id = payment.Id,
                returned_on_utc = "2026-02-08",
                amount = "25.00"
            }), CancellationToken.None);

            using var resp = await client.PostAsync($"/api/documents/{PropertyManagementCodes.ReceivableReturnedPayment}/{returned.Id}/post", null);
            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.receivable_returned_payment.original_payment_must_be_posted");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task PostAsync_WhenReturnedAmountExceedsRemainingUnappliedCredit_Returns400()
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

            var lease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
            {
                property_id = property.Id,
                start_on_utc = "2026-02-01",
                end_on_utc = "2026-02-28",
                rent_amount = "1000.00"
            }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);

            var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new NGB.Contracts.Common.PageRequestDto(0, 50, null), CancellationToken.None);
            var chargeType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));

            var charge = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                lease_id = lease.Id,
                charge_type_id = chargeType.Id,
                due_on_utc = "2026-02-05",
                amount = "100.00"
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                lease_id = lease.Id,
                received_on_utc = "2026-02-07",
                amount = "100.00"
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var apply = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableApply, Payload(new
            {
                credit_document_id = payment.Id,
                charge_document_id = charge.Id,
                applied_on_utc = "2026-02-08",
                amount = "90.00"
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivableApply, apply.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var returned = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableReturnedPayment, Payload(new
            {
                original_payment_id = payment.Id,
                returned_on_utc = "2026-02-09",
                amount = "20.00"
            }), CancellationToken.None);

            using var resp = await client.PostAsync($"/api/documents/{PropertyManagementCodes.ReceivableReturnedPayment}/{returned.Id}/post", null);
            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.receivable_returned_payment.insufficient_available_credit");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    private static async Task<(NGB.Contracts.Services.DocumentDto OriginalPayment, NGB.Contracts.Services.DocumentDto ReturnedPayment)> SeedDraftReturnedPaymentAsync(IServiceProvider services)
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
        var postedPayment = await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None);

        var returned = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableReturnedPayment, Payload(new
        {
            original_payment_id = payment.Id,
            returned_on_utc = "2026-02-08",
            amount = "25.00"
        }), CancellationToken.None);

        return (postedPayment, returned);
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
        catch { }
    }
}
