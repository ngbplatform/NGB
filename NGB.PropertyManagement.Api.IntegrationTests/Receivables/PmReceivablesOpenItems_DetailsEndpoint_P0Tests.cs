using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Runtime;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Receivables;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReceivablesOpenItems_DetailsEndpoint_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmReceivablesOpenItems_DetailsEndpoint_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetOpenItemsDetails_ReturnsDocDetails_NoNPlusOneNeeded()
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
                display = "Lease: P @ A",

                property_id = property.Id,
                start_on_utc = "2026-02-01",
                rent_amount = "1000.00"
            }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);

            var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
            var rentType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));

            var charge = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                display = "RC-1",
                lease_id = lease.Id,
                charge_type_id = rentType.Id,
                due_on_utc = "2026-02-05",
                amount = "100.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                display = "RP-1",
                lease_id = lease.Id,
                received_on_utc = "2026-02-07",
                amount = "120.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var apply = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableApply, Payload(new
            {
                display = "RA-1",
                credit_document_id = payment.Id,
                charge_document_id = charge.Id,
                applied_on_utc = "2026-02-07",
                amount = "70.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivableApply, apply.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var url = $"/api/receivables/open-items?leaseId={lease.Id}";
            var resp = await client.GetFromJsonAsync<ReceivablesOpenItemsDetailsResponse>(url);
            resp.Should().NotBeNull();

            resp!.Charges.Should().ContainSingle(x =>
                x.ChargeDocumentId == charge.Id &&
                x.DocumentType == PropertyManagementCodes.ReceivableCharge &&
                x.DueOnUtc == new DateOnly(2026, 2, 5) &&
                x.OutstandingAmount == 30m &&
                x.OriginalAmount == 100m &&
                string.Equals(x.ChargeTypeDisplay, "Utility", StringComparison.OrdinalIgnoreCase));

            resp.Credits.Should().ContainSingle(x =>
                x.CreditDocumentId == payment.Id &&
                x.DocumentType == PropertyManagementCodes.ReceivablePayment &&
                x.ReceivedOnUtc == new DateOnly(2026, 2, 7) &&
                x.AvailableCredit == 50m &&
                x.OriginalAmount == 120m);

            resp.Allocations.Should().ContainSingle(x =>
                x.ApplyId == apply.Id &&
                x.CreditDocumentId == payment.Id &&
                x.CreditDocumentType == PropertyManagementCodes.ReceivablePayment &&
                x.ChargeDocumentId == charge.Id &&
                x.AppliedOnUtc == new DateOnly(2026, 2, 7) &&
                x.Amount == 70m &&
                x.IsPosted);

            resp.Allocations[0].ApplyDisplay.Should().NotBeNullOrWhiteSpace();
            resp.Allocations[0].ApplyNumber.Should().NotBeNullOrWhiteSpace();
            resp.Allocations[0].CreditDocumentDisplay.Should().NotBeNullOrWhiteSpace();
            resp.Allocations[0].ChargeDisplay.Should().NotBeNullOrWhiteSpace();
            resp.Allocations[0].CreditDocumentNumber.Should().NotBeNullOrWhiteSpace();
            resp.Allocations[0].ChargeNumber.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task GetOpenItemsDetails_WhenOnePaymentAppliedToMultipleCharges_ReturnsMultipleAllocationRows()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            var seeded = await SeedLeaseAsync(setup, catalogs, documents, unitNo: "102", suffix: "multi");

            var charge1 = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                lease_id = seeded.Lease.Id,
                charge_type_id = seeded.RentType.Id,
                due_on_utc = "2026-02-05",
                amount = "100.00",
                memo = "Charge 1"
            }), CancellationToken.None);
            charge1 = await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge1.Id, CancellationToken.None);

            var charge2 = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                lease_id = seeded.Lease.Id,
                charge_type_id = seeded.RentType.Id,
                due_on_utc = "2026-02-10",
                amount = "80.00",
                memo = "Charge 2"
            }), CancellationToken.None);
            charge2 = await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge2.Id, CancellationToken.None);

            var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                lease_id = seeded.Lease.Id,
                received_on_utc = "2026-02-12",
                amount = "150.00",
                memo = "Payment"
            }), CancellationToken.None);
            payment = await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None);

            var apply1 = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableApply, Payload(new
            {
                credit_document_id = payment.Id,
                charge_document_id = charge1.Id,
                applied_on_utc = "2026-02-12",
                amount = "70.00"
            }), CancellationToken.None);
            apply1 = await documents.PostAsync(PropertyManagementCodes.ReceivableApply, apply1.Id, CancellationToken.None);

            var apply2 = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableApply, Payload(new
            {
                credit_document_id = payment.Id,
                charge_document_id = charge2.Id,
                applied_on_utc = "2026-02-13",
                amount = "60.00"
            }), CancellationToken.None);
            apply2 = await documents.PostAsync(PropertyManagementCodes.ReceivableApply, apply2.Id, CancellationToken.None);

            var resp = await client.GetFromJsonAsync<ReceivablesOpenItemsDetailsResponse>($"/api/receivables/open-items/details?leaseId={seeded.Lease.Id}");
            resp.Should().NotBeNull();

            resp!.Allocations.Should().HaveCount(2);
            resp.Allocations.Should().Contain(x => x.ApplyId == apply1.Id && x.CreditDocumentId == payment.Id && x.CreditDocumentType == PropertyManagementCodes.ReceivablePayment && x.ChargeDocumentId == charge1.Id && x.Amount == 70m);
            resp.Allocations.Should().Contain(x => x.ApplyId == apply2.Id && x.CreditDocumentId == payment.Id && x.CreditDocumentType == PropertyManagementCodes.ReceivablePayment && x.ChargeDocumentId == charge2.Id && x.Amount == 60m);
            resp.TotalOutstanding.Should().Be(50m);
            resp.TotalCredit.Should().Be(20m);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task GetOpenItemsDetails_AfterApplyUnpost_RemovesAllocationFromActiveList_AndRestoresOpenItems()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            var seeded = await SeedLeaseAsync(setup, catalogs, documents, unitNo: "103", suffix: "unapply");

            var charge = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                lease_id = seeded.Lease.Id,
                charge_type_id = seeded.RentType.Id,
                due_on_utc = "2026-02-05",
                amount = "100.00"
            }), CancellationToken.None);
            charge = await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge.Id, CancellationToken.None);

            var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                lease_id = seeded.Lease.Id,
                received_on_utc = "2026-02-07",
                amount = "120.00"
            }), CancellationToken.None);
            payment = await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None);

            var apply = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableApply, Payload(new
            {
                credit_document_id = payment.Id,
                charge_document_id = charge.Id,
                applied_on_utc = "2026-02-07",
                amount = "70.00"
            }), CancellationToken.None);
            apply = await documents.PostAsync(PropertyManagementCodes.ReceivableApply, apply.Id, CancellationToken.None);

            (await client.GetFromJsonAsync<ReceivablesOpenItemsDetailsResponse>($"/api/receivables/open-items/details?leaseId={seeded.Lease.Id}"))!
                .Allocations.Should().ContainSingle(x => x.ApplyId == apply.Id);

            (await documents.UnpostAsync(PropertyManagementCodes.ReceivableApply, apply.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Draft);

            var resp = await client.GetFromJsonAsync<ReceivablesOpenItemsDetailsResponse>($"/api/receivables/open-items/details?leaseId={seeded.Lease.Id}");
            resp.Should().NotBeNull();

            resp!.Allocations.Should().BeEmpty();
            resp.TotalOutstanding.Should().Be(100m);
            resp.TotalCredit.Should().Be(120m);
            resp.Charges.Should().ContainSingle(x => x.ChargeDocumentId == charge.Id && x.OutstandingAmount == 100m);
            resp.Credits.Should().ContainSingle(x => x.CreditDocumentId == payment.Id && x.AvailableCredit == 120m);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task GetOpenItemsDetails_WhenCreditMemoIsApplied_ReturnsCreditMemoAsCreditSource()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            var seeded = await SeedLeaseAsync(setup, catalogs, documents, unitNo: "105", suffix: "credit-memo");

            var charge = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                lease_id = seeded.Lease.Id,
                charge_type_id = seeded.RentType.Id,
                due_on_utc = "2026-02-05",
                amount = "100.00",
                memo = "Charge"
            }), CancellationToken.None);
            charge = await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge.Id, CancellationToken.None);

            var creditMemo = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCreditMemo, Payload(new
            {
                lease_id = seeded.Lease.Id,
                credited_on_utc = "2026-02-07",
                amount = "30.00",
                charge_type_id = seeded.RentType.Id,
                memo = "Credit memo"
            }), CancellationToken.None);
            creditMemo = await documents.PostAsync(PropertyManagementCodes.ReceivableCreditMemo, creditMemo.Id, CancellationToken.None);

            var apply = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableApply, Payload(new
            {
                credit_document_id = creditMemo.Id,
                charge_document_id = charge.Id,
                applied_on_utc = "2026-02-07",
                amount = "20.00"
            }), CancellationToken.None);
            apply = await documents.PostAsync(PropertyManagementCodes.ReceivableApply, apply.Id, CancellationToken.None);

            var resp = await client.GetFromJsonAsync<ReceivablesOpenItemsDetailsResponse>($"/api/receivables/open-items/details?leaseId={seeded.Lease.Id}");
            resp.Should().NotBeNull();

            resp!.Credits.Should().ContainSingle(x =>
                x.CreditDocumentId == creditMemo.Id
                && x.DocumentType == PropertyManagementCodes.ReceivableCreditMemo
                && x.AvailableCredit == 10m
                && x.OriginalAmount == 30m);

            resp.Allocations.Should().ContainSingle(x =>
                x.ApplyId == apply.Id
                && x.CreditDocumentId == creditMemo.Id
                && x.CreditDocumentType == PropertyManagementCodes.ReceivableCreditMemo
                && x.ChargeDocumentId == charge.Id
                && x.Amount == 20m
                && x.IsPosted);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }


    [Fact]
    public async Task GetOpenItemsDetails_WhenRentChargeIsApplied_ReturnsRentDocumentType_AndAllocationContext()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            var seeded = await SeedLeaseAsync(setup, catalogs, documents, unitNo: "104", suffix: "rent");

            var rentCharge = await documents.CreateDraftAsync(PropertyManagementCodes.RentCharge, Payload(new
            {
                lease_id = seeded.Lease.Id,
                period_from_utc = "2026-02-01",
                period_to_utc = "2026-02-28",
                due_on_utc = "2026-02-05",
                amount = "100.00",
                memo = "Rent"
            }), CancellationToken.None);
            rentCharge = await documents.PostAsync(PropertyManagementCodes.RentCharge, rentCharge.Id, CancellationToken.None);

            var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                lease_id = seeded.Lease.Id,
                received_on_utc = "2026-02-07",
                amount = "100.00",
                memo = "Payment"
            }), CancellationToken.None);
            payment = await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None);

            var apply = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableApply, Payload(new
            {
                credit_document_id = payment.Id,
                charge_document_id = rentCharge.Id,
                applied_on_utc = "2026-02-07",
                amount = "100.00"
            }), CancellationToken.None);
            apply = await documents.PostAsync(PropertyManagementCodes.ReceivableApply, apply.Id, CancellationToken.None);

            var resp = await client.GetFromJsonAsync<ReceivablesOpenItemsDetailsResponse>($"/api/receivables/open-items/details?leaseId={seeded.Lease.Id}");
            resp.Should().NotBeNull();

            resp!.Charges.Should().BeEmpty();
            resp.Credits.Should().BeEmpty();
            resp.Allocations.Should().ContainSingle(x =>
                x.ApplyId == apply.Id
                && x.CreditDocumentId == payment.Id
                && x.CreditDocumentType == PropertyManagementCodes.ReceivablePayment
                && x.ChargeDocumentId == rentCharge.Id
                && x.ChargeDocumentType == PropertyManagementCodes.RentCharge
                && x.Amount == 100m
                && x.IsPosted);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    private static async Task<SeededLeaseContext> SeedLeaseAsync(
        IPropertyManagementSetupService setup,
        ICatalogService catalogs,
        IDocumentService documents,
        string unitNo,
        string suffix)
    {
        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = $"Party {suffix}" }), CancellationToken.None);
        var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            display = $"Building {suffix}",
            address_line1 = $"{suffix} Main St",
            city = "Hoboken",
            state = "NJ",
            zip = "07030"
        }), CancellationToken.None);

        var property = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Unit",
            parent_property_id = building.Id,
            unit_no = unitNo
        }), CancellationToken.None);

        var lease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
        {
            property_id = property.Id,
            start_on_utc = "2026-02-01",
            rent_amount = "1000.00"
        }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);

        var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
        var rentType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));

        return new SeededLeaseContext(party, property, lease, rentType);
    }

    private sealed record SeededLeaseContext(
        NGB.Contracts.Services.CatalogItemDto Party,
        NGB.Contracts.Services.CatalogItemDto Property,
        NGB.Contracts.Services.DocumentDto Lease,
        NGB.Contracts.Services.CatalogItemDto RentType);

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
