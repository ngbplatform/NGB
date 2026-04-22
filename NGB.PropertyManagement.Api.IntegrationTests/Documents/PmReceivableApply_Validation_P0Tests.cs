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
public sealed class PmReceivableApply_Validation_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmReceivableApply_Validation_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

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
                $"/api/documents/{PropertyManagementCodes.ReceivableApply}",
                new
                {
                    fields = new
                    {
                        credit_document_id = payment.Id,
                        charge_document_id = charge.Id,
                        applied_on_utc = "2026-02-07",
                        amount = 0m
                    }
                });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.receivables.apply.amount_must_be_positive");
            root.GetProperty("detail").GetString().Should().Be("Apply amount must be positive. Actual: 0.");
            root.GetProperty("error").GetProperty("errors").GetProperty("amount").EnumerateArray().Select(x => x.GetString())
                .Should().Contain("Amount must be positive.");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task Create_WhenChargeAndPaymentBelongToDifferentLease_Returns400_WithFriendlyFieldErrors()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            var (payment, _, chargeOtherLease) = await CreatePostedPaymentAndTwoChargesAsync(setup, catalogs, documents);

            using var resp = await client.PostAsJsonAsync(
                $"/api/documents/{PropertyManagementCodes.ReceivableApply}",
                new
                {
                    fields = new
                    {
                        credit_document_id = payment.Id,
                        charge_document_id = chargeOtherLease.Id,
                        applied_on_utc = "2026-02-07",
                        amount = 10m
                    }
                });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.receivables.apply.party_property_lease_mismatch");
            root.GetProperty("detail").GetString().Should().Be("Credit source and charge must belong to the same party/property/lease.");

            var errors = root.GetProperty("error").GetProperty("errors");
            errors.GetProperty("credit_document_id").EnumerateArray().Select(x => x.GetString())
                .Should().Contain("Credit source and charge must belong to the same lease.");
            errors.GetProperty("charge_document_id").EnumerateArray().Select(x => x.GetString())
                .Should().Contain("Credit source and charge must belong to the same lease.");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task Update_WhenPartialChangeBreaksMergedSnapshot_Returns400_WithFriendlyFieldError()
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

            var (payment, charge, chargeOtherLease) = await CreatePostedPaymentAndTwoChargesAsync(setup, catalogs, documents);

            var apply = await documents.CreateDraftAsync(
                PropertyManagementCodes.ReceivableApply,
                Payload(new
                {
                    credit_document_id = payment.Id,
                    charge_document_id = charge.Id,
                    applied_on_utc = "2026-02-07",
                    amount = "10.00"
                }),
                CancellationToken.None);

            using var resp = await client.PutAsJsonAsync(
                $"/api/documents/{PropertyManagementCodes.ReceivableApply}/{apply.Id}",
                new
                {
                    fields = new
                    {
                        charge_document_id = chargeOtherLease.Id
                    }
                });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.receivables.apply.party_property_lease_mismatch");
            root.GetProperty("error").GetProperty("errors").GetProperty("charge_document_id").EnumerateArray().Select(x => x.GetString())
                .Should().Contain("Credit source and charge must belong to the same lease.");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task Create_WhenPaymentReferenceIsWrongDocumentType_Returns400_WithFriendlyFieldError()
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

            var (party, unit, lease, rentType) = await CreateLeaseAndRentTypeAsync(setup, catalogs, documents, suffix: "A", unitNo: "101");

            var charge = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                lease_id = lease.Id,
                charge_type_id = rentType.Id,
                due_on_utc = "2026-02-05",
                amount = "100.00",
                memo = "Charge A"
            }), CancellationToken.None);
            charge = await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge.Id, CancellationToken.None);

            using var resp = await client.PostAsJsonAsync(
                $"/api/documents/{PropertyManagementCodes.ReceivableApply}",
                new
                {
                    fields = new
                    {
                        credit_document_id = lease.Id,
                        charge_document_id = charge.Id,
                        applied_on_utc = "2026-02-07",
                        amount = 10m
                    }
                });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.receivables.apply.payment_wrong_type");
            root.GetProperty("detail").GetString().Should().Be("Selected document is not a receivable credit source.");
            root.GetProperty("error").GetProperty("errors").GetProperty("credit_document_id").EnumerateArray().Select(x => x.GetString())
                .Should().Contain("Selected document is not a receivable credit source.");
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

    private static async Task<(DocumentDto Payment, DocumentDto Charge, DocumentDto ChargeOtherLease)> CreatePostedPaymentAndTwoChargesAsync(
        IPropertyManagementSetupService setup,
        ICatalogService catalogs,
        IDocumentService documents)
    {
        var (partyA, unitA, leaseA, rentType) = await CreateLeaseAndRentTypeAsync(setup, catalogs, documents, suffix: "A", unitNo: "101");
        var (partyB, unitB, leaseB, _) = await CreateLeaseAndRentTypeAsync(setup, catalogs, documents, suffix: "B", unitNo: "102");

        var charge = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
        {
            lease_id = leaseA.Id,
            charge_type_id = rentType.Id,
            due_on_utc = "2026-02-05",
            amount = "100.00",
            memo = "Charge A"
        }), CancellationToken.None);
        charge = await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge.Id, CancellationToken.None);

        var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
        {
            lease_id = leaseA.Id,
            received_on_utc = "2026-02-07",
            amount = "100.00",
            memo = "Payment A"
        }), CancellationToken.None);
        payment = await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None);

        var chargeOtherLease = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
        {
            lease_id = leaseB.Id,
            charge_type_id = rentType.Id,
            due_on_utc = "2026-02-06",
            amount = "80.00",
            memo = "Charge B"
        }), CancellationToken.None);
        chargeOtherLease = await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, chargeOtherLease.Id, CancellationToken.None);

        return (payment, charge, chargeOtherLease);
    }

    private static async Task<(CatalogItemDto Party, CatalogItemDto Unit, DocumentDto Lease, CatalogItemDto RentType)> CreateLeaseAndRentTypeAsync(
        IPropertyManagementSetupService setup,
        ICatalogService catalogs,
        IDocumentService documents,
        string suffix = "A",
        string unitNo = "101")
    {
        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = $"Party {suffix}" }), CancellationToken.None);
        var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            display = $"Building {suffix}",
            address_line1 = $"{suffix} Main",
            city = "Hoboken",
            state = "NJ",
            zip = "07030"
        }), CancellationToken.None);

        var unit = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Unit",
            parent_property_id = building.Id,
            unit_no = unitNo
        }), CancellationToken.None);

        var lease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
        {
            property_id = unit.Id,
            start_on_utc = "2026-02-01",
            rent_amount = "1000.00"
        }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);
        lease = await documents.PostAsync(PropertyManagementCodes.Lease, lease.Id, CancellationToken.None);

        var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new NGB.Contracts.Common.PageRequestDto(0, 50, null), CancellationToken.None);
        var rentType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));

        return (party, unit, lease, rentType);
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
