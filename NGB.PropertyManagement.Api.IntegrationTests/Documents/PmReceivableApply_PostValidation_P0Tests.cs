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
public sealed class PmReceivableApply_PostValidation_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmReceivableApply_PostValidation_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Post_WhenPaymentWasUnpostedAfterDraft_Returns400_WithFriendlyFieldError()
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

            var (payment, charge) = await CreatePostedPaymentAndChargeAsync(catalogs, documents);
            var apply = await documents.CreateDraftAsync(
                PropertyManagementCodes.ReceivableApply,
                Payload(new
                {
                    credit_document_id = payment.Id,
                    charge_document_id = charge.Id,
                    applied_on_utc = "2026-02-07",
                    amount = "10.00",
                    memo = "Apply draft"
                }),
                CancellationToken.None);

            var paymentUnposted = await documents.UnpostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None);
            paymentUnposted.Status.Should().Be(DocumentStatus.Draft);

            using var resp = await client.PostAsync($"/api/documents/{PropertyManagementCodes.ReceivableApply}/{apply.Id}/post", content: null);

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.receivables.apply.payment_must_be_posted");
            root.GetProperty("detail").GetString().Should().Be("Selected credit source must be posted before it can be applied.");
            root.GetProperty("error").GetProperty("errors").GetProperty("credit_document_id").EnumerateArray().Select(x => x.GetString())
                .Should().Contain("Selected credit source must be posted before it can be applied.");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task Post_WhenChargeWasUnpostedAfterDraft_Returns400_WithFriendlyFieldError()
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

            var (payment, charge) = await CreatePostedPaymentAndChargeAsync(catalogs, documents);
            var apply = await documents.CreateDraftAsync(
                PropertyManagementCodes.ReceivableApply,
                Payload(new
                {
                    credit_document_id = payment.Id,
                    charge_document_id = charge.Id,
                    applied_on_utc = "2026-02-07",
                    amount = "10.00",
                    memo = "Apply draft"
                }),
                CancellationToken.None);

            var chargeUnposted = await documents.UnpostAsync(PropertyManagementCodes.ReceivableCharge, charge.Id, CancellationToken.None);
            chargeUnposted.Status.Should().Be(DocumentStatus.Draft);

            using var resp = await client.PostAsync($"/api/documents/{PropertyManagementCodes.ReceivableApply}/{apply.Id}/post", content: null);

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.receivables.apply.charge_must_be_posted");
            root.GetProperty("detail").GetString().Should().Be("Selected charge must be posted before it can be applied.");
            root.GetProperty("error").GetProperty("errors").GetProperty("charge_document_id").EnumerateArray().Select(x => x.GetString())
                .Should().Contain("Selected charge must be posted before it can be applied.");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task Post_WhenChargeIsRentCharge_AllowsPostingApplyAgainstRentCharge()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            await using var scope = factory.Services.CreateAsyncScope();

            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            await setup.EnsureDefaultsAsync(CancellationToken.None);

            var (payment, rentCharge) = await CreatePostedPaymentAndRentChargeAsync(catalogs, documents);
            var apply = await documents.CreateDraftAsync(
                PropertyManagementCodes.ReceivableApply,
                Payload(new
                {
                    credit_document_id = payment.Id,
                    charge_document_id = rentCharge.Id,
                    applied_on_utc = "2026-02-07",
                    amount = "10.00",
                    memo = "Apply rent charge"
                }),
                CancellationToken.None);

            var posted = await documents.PostAsync(PropertyManagementCodes.ReceivableApply, apply.Id, CancellationToken.None);
            posted.Status.Should().Be(DocumentStatus.Posted);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task GetByIdAndPage_WhenChargeIsRentCharge_ReturnsChargeRefWithDisplay()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            await using var scope = factory.Services.CreateAsyncScope();

            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            await setup.EnsureDefaultsAsync(CancellationToken.None);

            var (payment, rentCharge) = await CreatePostedPaymentAndRentChargeAsync(catalogs, documents);
            var apply = await documents.CreateDraftAsync(
                PropertyManagementCodes.ReceivableApply,
                Payload(new
                {
                    credit_document_id = payment.Id,
                    charge_document_id = rentCharge.Id,
                    applied_on_utc = "2026-02-07",
                    amount = "10.00",
                    memo = "Apply rent charge"
                }),
                CancellationToken.None);

            await documents.PostAsync(PropertyManagementCodes.ReceivableApply, apply.Id, CancellationToken.None);

            var byId = await documents.GetByIdAsync(PropertyManagementCodes.ReceivableApply, apply.Id, CancellationToken.None);
            AssertRichRef(byId.Payload.Fields!["charge_document_id"], rentCharge.Id);

            var page = await documents.GetPageAsync(PropertyManagementCodes.ReceivableApply, new PageRequestDto(0, 50, null), CancellationToken.None);
            var listed = page.Items.Single(x => x.Id == apply.Id);
            AssertRichRef(listed.Payload.Fields!["charge_document_id"], rentCharge.Id);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task GetByIdAndPage_WhenChargeIsLateFeeCharge_ReturnsChargeRefWithDisplay()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            await using var scope = factory.Services.CreateAsyncScope();

            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            await setup.EnsureDefaultsAsync(CancellationToken.None);

            var (payment, lateFeeCharge) = await CreatePostedPaymentAndLateFeeChargeAsync(catalogs, documents);
            var apply = await documents.CreateDraftAsync(
                PropertyManagementCodes.ReceivableApply,
                Payload(new
                {
                    credit_document_id = payment.Id,
                    charge_document_id = lateFeeCharge.Id,
                    applied_on_utc = "2026-02-07",
                    amount = "10.00",
                    memo = "Apply late fee charge"
                }),
                CancellationToken.None);

            await documents.PostAsync(PropertyManagementCodes.ReceivableApply, apply.Id, CancellationToken.None);

            var byId = await documents.GetByIdAsync(PropertyManagementCodes.ReceivableApply, apply.Id, CancellationToken.None);
            AssertRichRef(byId.Payload.Fields!["charge_document_id"], lateFeeCharge.Id);

            var page = await documents.GetPageAsync(PropertyManagementCodes.ReceivableApply, new PageRequestDto(0, 50, null), CancellationToken.None);
            var listed = page.Items.Single(x => x.Id == apply.Id);
            AssertRichRef(listed.Payload.Fields!["charge_document_id"], lateFeeCharge.Id);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    private static async Task<(DocumentDto Payment, DocumentDto Charge)> CreatePostedPaymentAndChargeAsync(
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
        lease = await documents.PostAsync(PropertyManagementCodes.Lease, lease.Id, CancellationToken.None);

        var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
        var rentType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));

        var charge = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
        {
            lease_id = lease.Id,
            charge_type_id = rentType.Id,
            due_on_utc = "2026-02-05",
            amount = "100.00",
            memo = "Charge"
        }), CancellationToken.None);
        charge = await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge.Id, CancellationToken.None);

        var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
        {
            lease_id = lease.Id,
            received_on_utc = "2026-02-07",
            amount = "100.00",
            memo = "Payment"
        }), CancellationToken.None);
        payment = await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None);

        return (payment, charge);
    }

    private static async Task<(DocumentDto Payment, DocumentDto RentCharge)> CreatePostedPaymentAndRentChargeAsync(
        ICatalogService catalogs,
        IDocumentService documents)
    {
        var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "P-Rent" }), CancellationToken.None);
        var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            display = "A-Rent",
            address_line1 = "A-Rent",
            city = "Hoboken",
            state = "NJ",
            zip = "07030"
        }), CancellationToken.None);

        var unit = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Unit",
            parent_property_id = building.Id,
            unit_no = "201"
        }), CancellationToken.None);

        var lease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
        {
            property_id = unit.Id,
            start_on_utc = "2026-02-01",
            end_on_utc = "2026-02-28",
            rent_amount = "1000.00"
        }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);
        lease = await documents.PostAsync(PropertyManagementCodes.Lease, lease.Id, CancellationToken.None);

        var rentCharge = await documents.CreateDraftAsync(PropertyManagementCodes.RentCharge, Payload(new
        {
            lease_id = lease.Id,
            period_from_utc = "2026-02-01",
            period_to_utc = "2026-02-28",
            due_on_utc = "2026-02-05",
            amount = "100.00",
            memo = "Rent"
        }), CancellationToken.None);
        rentCharge = await documents.PostAsync(PropertyManagementCodes.RentCharge, rentCharge.Id, CancellationToken.None);

        var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
        {
            lease_id = lease.Id,
            received_on_utc = "2026-02-07",
            amount = "100.00",
            memo = "Payment"
        }), CancellationToken.None);
        payment = await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None);

        return (payment, rentCharge);
    }

    private static async Task<(DocumentDto Payment, DocumentDto LateFeeCharge)> CreatePostedPaymentAndLateFeeChargeAsync(
        ICatalogService catalogs,
        IDocumentService documents)
    {
        var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "P-Late" }), CancellationToken.None);
        var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            display = "A-Late",
            address_line1 = "A-Late",
            city = "Hoboken",
            state = "NJ",
            zip = "07030"
        }), CancellationToken.None);

        var unit = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Unit",
            parent_property_id = building.Id,
            unit_no = "301"
        }), CancellationToken.None);

        var lease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
        {
            property_id = unit.Id,
            start_on_utc = "2026-02-01",
            end_on_utc = "2026-02-28",
            rent_amount = "1000.00"
        }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);
        lease = await documents.PostAsync(PropertyManagementCodes.Lease, lease.Id, CancellationToken.None);

        var lateFeeCharge = await documents.CreateDraftAsync(PropertyManagementCodes.LateFeeCharge, Payload(new
        {
            lease_id = lease.Id,
            due_on_utc = "2026-02-05",
            amount = "25.00",
            memo = "Late fee"
        }), CancellationToken.None);
        lateFeeCharge = await documents.PostAsync(PropertyManagementCodes.LateFeeCharge, lateFeeCharge.Id, CancellationToken.None);

        var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
        {
            lease_id = lease.Id,
            received_on_utc = "2026-02-07",
            amount = "100.00",
            memo = "Payment"
        }), CancellationToken.None);
        payment = await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None);

        return (payment, lateFeeCharge);
    }

    private static void AssertRichRef(JsonElement value, Guid expectedId)
    {
        value.ValueKind.Should().Be(JsonValueKind.Object);
        value.GetProperty("id").GetGuid().Should().Be(expectedId);
        value.GetProperty("display").GetString().Should().NotBeNullOrWhiteSpace();
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
