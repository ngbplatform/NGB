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

namespace NGB.PropertyManagement.Api.IntegrationTests.Payables;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmPayables_Effects_UiActions_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmPayables_Effects_UiActions_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Effects_UiActions_CanApply_DependsOnOutstandingOrCredit()
    {
        using var factory = new PmApiFactory(_fixture);
        using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        await using var scope = factory.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var vendor = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "Vendor", is_vendor = true, is_tenant = false }), CancellationToken.None);
        var property = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            address_line1 = "1 Effect Way",
            city = "Hoboken",
            state = "NJ",
            zip = "07030"
        }), CancellationToken.None);

        var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.PayableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
        var repairType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Repair", StringComparison.OrdinalIgnoreCase));

        var c1 = await documents.CreateDraftAsync(PropertyManagementCodes.PayableCharge, Payload(new
        {
            party_id = vendor.Id,
            property_id = property.Id,
            charge_type_id = repairType.Id,
            due_on_utc = "2026-03-05",
            amount = "100.00",
            vendor_invoice_no = "INV-E1"
        }), CancellationToken.None);
        (await documents.PostAsync(PropertyManagementCodes.PayableCharge, c1.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

        var c2 = await documents.CreateDraftAsync(PropertyManagementCodes.PayableCharge, Payload(new
        {
            party_id = vendor.Id,
            property_id = property.Id,
            charge_type_id = repairType.Id,
            due_on_utc = "2026-03-06",
            amount = "20.00",
            vendor_invoice_no = "INV-E2"
        }), CancellationToken.None);
        (await documents.PostAsync(PropertyManagementCodes.PayableCharge, c2.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

        var payment = await documents.CreateDraftAsync(PropertyManagementCodes.PayablePayment, Payload(new
        {
            party_id = vendor.Id,
            property_id = property.Id,
            paid_on_utc = "2026-03-07",
            amount = "120.00"
        }), CancellationToken.None);
        (await documents.PostAsync(PropertyManagementCodes.PayablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

        (await documents.GetEffectsAsync(PropertyManagementCodes.PayableCharge, c1.Id, 100, CancellationToken.None)).Ui!.CanApply.Should().BeTrue();
        (await documents.GetEffectsAsync(PropertyManagementCodes.PayablePayment, payment.Id, 100, CancellationToken.None)).Ui!.CanApply.Should().BeTrue();

        var a1 = await documents.CreateDraftAsync(PropertyManagementCodes.PayableApply, Payload(new
        {
            credit_document_id = payment.Id,
            charge_document_id = c1.Id,
            applied_on_utc = "2026-03-07",
            amount = "70.00"
        }), CancellationToken.None);
        (await documents.PostAsync(PropertyManagementCodes.PayableApply, a1.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

        (await documents.GetEffectsAsync(PropertyManagementCodes.PayableCharge, c1.Id, 100, CancellationToken.None)).Ui!.CanApply.Should().BeTrue();
        (await documents.GetEffectsAsync(PropertyManagementCodes.PayablePayment, payment.Id, 100, CancellationToken.None)).Ui!.CanApply.Should().BeTrue();

        var a2 = await documents.CreateDraftAsync(PropertyManagementCodes.PayableApply, Payload(new
        {
            credit_document_id = payment.Id,
            charge_document_id = c1.Id,
            applied_on_utc = "2026-03-08",
            amount = "30.00"
        }), CancellationToken.None);
        (await documents.PostAsync(PropertyManagementCodes.PayableApply, a2.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

        var a3 = await documents.CreateDraftAsync(PropertyManagementCodes.PayableApply, Payload(new
        {
            credit_document_id = payment.Id,
            charge_document_id = c2.Id,
            applied_on_utc = "2026-03-08",
            amount = "20.00"
        }), CancellationToken.None);
        (await documents.PostAsync(PropertyManagementCodes.PayableApply, a3.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

        var c1Effects = await documents.GetEffectsAsync(PropertyManagementCodes.PayableCharge, c1.Id, 100, CancellationToken.None);
        c1Effects.Ui!.CanApply.Should().BeFalse();
        c1Effects.Ui.DisabledReasons["apply"].Single().ErrorCode.Should().Be("pm.ui.payables.apply.no_outstanding");

        var c2Effects = await documents.GetEffectsAsync(PropertyManagementCodes.PayableCharge, c2.Id, 100, CancellationToken.None);
        c2Effects.Ui!.CanApply.Should().BeFalse();
        c2Effects.Ui.DisabledReasons["apply"].Single().ErrorCode.Should().Be("pm.ui.payables.apply.no_outstanding");

        var pEffects = await documents.GetEffectsAsync(PropertyManagementCodes.PayablePayment, payment.Id, 100, CancellationToken.None);
        pEffects.Ui!.CanApply.Should().BeFalse();
        pEffects.Ui.DisabledReasons["apply"].Single().ErrorCode.Should().Be("pm.ui.payables.apply.no_credit");
    }

    [Fact]
    public async Task Effects_UiActions_PayableCreditMemo_CanApply_WhenAvailableCreditExists()
    {
        using var factory = new PmApiFactory(_fixture);
        using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        await using var scope = factory.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var vendor = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "Vendor CM", is_vendor = true, is_tenant = false }), CancellationToken.None);
        var property = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            address_line1 = "2 Effect Way",
            city = "Hoboken",
            state = "NJ",
            zip = "07030"
        }), CancellationToken.None);

        var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.PayableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
        var repairType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Repair", StringComparison.OrdinalIgnoreCase));

        var charge = await documents.CreateDraftAsync(PropertyManagementCodes.PayableCharge, Payload(new
        {
            party_id = vendor.Id,
            property_id = property.Id,
            charge_type_id = repairType.Id,
            due_on_utc = "2026-03-05",
            amount = "60.00",
            vendor_invoice_no = "INV-E3"
        }), CancellationToken.None);
        (await documents.PostAsync(PropertyManagementCodes.PayableCharge, charge.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

        var creditMemo = await documents.CreateDraftAsync(PropertyManagementCodes.PayableCreditMemo, Payload(new
        {
            party_id = vendor.Id,
            property_id = property.Id,
            charge_type_id = repairType.Id,
            credited_on_utc = "2026-03-07",
            amount = "60.00"
        }), CancellationToken.None);
        (await documents.PostAsync(PropertyManagementCodes.PayableCreditMemo, creditMemo.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

        (await documents.GetEffectsAsync(PropertyManagementCodes.PayableCreditMemo, creditMemo.Id, 100, CancellationToken.None)).Ui!.CanApply.Should().BeTrue();

        var apply = await documents.CreateDraftAsync(PropertyManagementCodes.PayableApply, Payload(new
        {
            credit_document_id = creditMemo.Id,
            charge_document_id = charge.Id,
            applied_on_utc = "2026-03-08",
            amount = "60.00"
        }), CancellationToken.None);
        (await documents.PostAsync(PropertyManagementCodes.PayableApply, apply.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

        var effects = await documents.GetEffectsAsync(PropertyManagementCodes.PayableCreditMemo, creditMemo.Id, 100, CancellationToken.None);
        effects.Ui!.CanApply.Should().BeFalse();
        effects.Ui.DisabledReasons["apply"].Single().ErrorCode.Should().Be("pm.ui.payables.apply.no_credit");
    }

    private static RecordPayload Payload(object obj)
    {
        var el = JsonSerializer.SerializeToElement(obj);
        var dict = new Dictionary<string, JsonElement>();
        foreach (var p in el.EnumerateObject())
            dict[p.Name] = p.Value;
        return new RecordPayload(dict, null);
    }
}
