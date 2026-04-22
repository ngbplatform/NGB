using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Runtime;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmPayableApply_Validation_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmPayableApply_Validation_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

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

            var (payment, charge) = await CreatePostedPaymentAndChargeAsync(setup, catalogs, documents);

            using var resp = await client.PostAsJsonAsync(
                $"/api/documents/{PropertyManagementCodes.PayableApply}",
                new
                {
                    fields = new
                    {
                        credit_document_id = payment.Id,
                        charge_document_id = charge.Id,
                        applied_on_utc = "2026-03-07",
                        amount = 0m
                    }
                });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.payables.apply.amount_must_be_positive");
            root.GetProperty("error").GetProperty("errors").GetProperty("amount").EnumerateArray().Select(x => x.GetString())
                .Should().Contain("Amount must be positive.");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task Create_WhenPaymentAndChargeBelongToDifferentVendorOrProperty_Returns400_WithFriendlyFieldErrors()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            var (payment, _, otherCharge) = await CreatePostedPaymentAndTwoChargesAsync(setup, catalogs, documents);

            using var resp = await client.PostAsJsonAsync(
                $"/api/documents/{PropertyManagementCodes.PayableApply}",
                new
                {
                    fields = new
                    {
                        credit_document_id = payment.Id,
                        charge_document_id = otherCharge.Id,
                        applied_on_utc = "2026-03-07",
                        amount = 10m
                    }
                });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.payables.apply.party_property_mismatch");
            root.GetProperty("detail").GetString().Should().Be("Credit source and charge must belong to the same vendor/property.");

            var errors = root.GetProperty("error").GetProperty("errors");
            errors.GetProperty("credit_document_id").EnumerateArray().Select(x => x.GetString())
                .Should().Contain("Credit source and charge must belong to the same vendor/property.");
            errors.GetProperty("charge_document_id").EnumerateArray().Select(x => x.GetString())
                .Should().Contain("Credit source and charge must belong to the same vendor/property.");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    private static async Task<(DocumentDto Payment, DocumentDto Charge)> CreatePostedPaymentAndChargeAsync(
        IPropertyManagementSetupService setup,
        ICatalogService catalogs,
        IDocumentService documents)
    {
        var (payment, charge, _) = await CreatePostedPaymentAndTwoChargesAsync(setup, catalogs, documents);
        return (payment, charge);
    }

    private static async Task<(DocumentDto Payment, DocumentDto Charge, DocumentDto ChargeOtherContext)> CreatePostedPaymentAndTwoChargesAsync(
        IPropertyManagementSetupService setup,
        ICatalogService catalogs,
        IDocumentService documents)
    {
        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var vendor = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "Vendor A", is_vendor = true, is_tenant = false }), CancellationToken.None);
        var vendorB = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "Vendor B", is_vendor = true, is_tenant = false }), CancellationToken.None);

        var propertyA = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            address_line1 = "1 Demo Way",
            city = "Hoboken",
            state = "NJ",
            zip = "07030"
        }), CancellationToken.None);

        var propertyB = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            address_line1 = "2 Demo Way",
            city = "Hoboken",
            state = "NJ",
            zip = "07030"
        }), CancellationToken.None);

        var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.PayableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
        var repairType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Repair", StringComparison.OrdinalIgnoreCase));

        var charge = await documents.CreateDraftAsync(PropertyManagementCodes.PayableCharge, Payload(new
        {
            party_id = vendor.Id,
            property_id = propertyA.Id,
            charge_type_id = repairType.Id,
            due_on_utc = "2026-03-05",
            amount = "100.00",
            vendor_invoice_no = "INV-100",
            memo = "Charge A"
        }), CancellationToken.None);
        charge = await documents.PostAsync(PropertyManagementCodes.PayableCharge, charge.Id, CancellationToken.None);

        var otherCharge = await documents.CreateDraftAsync(PropertyManagementCodes.PayableCharge, Payload(new
        {
            party_id = vendorB.Id,
            property_id = propertyB.Id,
            charge_type_id = repairType.Id,
            due_on_utc = "2026-03-06",
            amount = "80.00",
            vendor_invoice_no = "INV-200",
            memo = "Charge B"
        }), CancellationToken.None);
        otherCharge = await documents.PostAsync(PropertyManagementCodes.PayableCharge, otherCharge.Id, CancellationToken.None);

        var payment = await documents.CreateDraftAsync(PropertyManagementCodes.PayablePayment, Payload(new
        {
            party_id = vendor.Id,
            property_id = propertyA.Id,
            paid_on_utc = "2026-03-07",
            amount = "100.00",
            memo = "Vendor payment"
        }), CancellationToken.None);
        payment = await documents.PostAsync(PropertyManagementCodes.PayablePayment, payment.Id, CancellationToken.None);

        return (payment, charge, otherCharge);
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
