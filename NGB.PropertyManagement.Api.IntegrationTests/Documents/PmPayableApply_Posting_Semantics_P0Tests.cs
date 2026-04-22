using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Core.Dimensions;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Runtime;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmPayableApply_Posting_Semantics_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmPayableApply_Posting_Semantics_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PostAsync_AllocatesCreditBetweenOpenItems_AndPreventsOverApply()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
            var opregRead = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();
            var policyReader = scope.ServiceProvider.GetRequiredService<IPropertyManagementAccountingPolicyReader>();

            await setup.EnsureDefaultsAsync(CancellationToken.None);
            var policy = await policyReader.GetRequiredAsync(CancellationToken.None);

            var vendor = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "Vendor", is_vendor = true, is_tenant = false }), CancellationToken.None);
            var property = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
            {
                kind = "Building",
                address_line1 = "1 Demo Way",
                city = "Hoboken",
                state = "NJ",
                zip = "07030"
            }), CancellationToken.None);

            var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.PayableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
            var repairType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Repair", StringComparison.OrdinalIgnoreCase));

            var charge1 = await documents.CreateDraftAsync(PropertyManagementCodes.PayableCharge, Payload(new
            {
                party_id = vendor.Id,
                property_id = property.Id,
                charge_type_id = repairType.Id,
                due_on_utc = "2026-03-05",
                amount = "100.00",
                vendor_invoice_no = "INV-100",
                memo = "m"
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.PayableCharge, charge1.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var charge2 = await documents.CreateDraftAsync(PropertyManagementCodes.PayableCharge, Payload(new
            {
                party_id = vendor.Id,
                property_id = property.Id,
                charge_type_id = repairType.Id,
                due_on_utc = "2026-03-06",
                amount = "80.00",
                vendor_invoice_no = "INV-200",
                memo = "m"
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.PayableCharge, charge2.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var payment = await documents.CreateDraftAsync(PropertyManagementCodes.PayablePayment, Payload(new
            {
                party_id = vendor.Id,
                property_id = property.Id,
                paid_on_utc = "2026-03-07",
                amount = "100.00",
                memo = "m"
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.PayablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var apply1 = await documents.CreateDraftAsync(PropertyManagementCodes.PayableApply, Payload(new
            {
                credit_document_id = payment.Id,
                charge_document_id = charge1.Id,
                applied_on_utc = "2026-03-07",
                amount = "60.00",
                memo = "m"
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.PayableApply, apply1.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var itemDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.PayableItem}");

            var charge1Outstanding = await GetNetAmountForItemAsync(opregRead, policy.PayablesOpenItemsOperationalRegisterId, itemDimId, charge1.Id);
            charge1Outstanding.Should().Be(40m);

            var paymentNet = await GetNetAmountForItemAsync(opregRead, policy.PayablesOpenItemsOperationalRegisterId, itemDimId, payment.Id);
            paymentNet.Should().Be(-40m);

            var overCharge = await documents.CreateDraftAsync(PropertyManagementCodes.PayableApply, Payload(new
            {
                credit_document_id = payment.Id,
                charge_document_id = charge1.Id,
                applied_on_utc = "2026-03-07",
                amount = "50.00",
            }), CancellationToken.None);

            var actOverCharge = async () => await documents.PostAsync(PropertyManagementCodes.PayableApply, overCharge.Id, CancellationToken.None);
            await actOverCharge.Should().ThrowAsync<PayableApplyValidationException>();

            var overCredit = await documents.CreateDraftAsync(PropertyManagementCodes.PayableApply, Payload(new
            {
                credit_document_id = payment.Id,
                charge_document_id = charge2.Id,
                applied_on_utc = "2026-03-07",
                amount = "50.00",
            }), CancellationToken.None);

            var actOverCredit = async () => await documents.PostAsync(PropertyManagementCodes.PayableApply, overCredit.Id, CancellationToken.None);
            await actOverCredit.Should().ThrowAsync<PayableApplyValidationException>();
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task PostAsync_AppliesStandaloneCreditMemo_AsCreditSource()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
            var opregRead = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();
            var policyReader = scope.ServiceProvider.GetRequiredService<IPropertyManagementAccountingPolicyReader>();

            await setup.EnsureDefaultsAsync(CancellationToken.None);
            var policy = await policyReader.GetRequiredAsync(CancellationToken.None);

            var vendor = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "Vendor", is_vendor = true, is_tenant = false }), CancellationToken.None);
            var property = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
            {
                kind = "Building",
                address_line1 = "9 Demo Way",
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
                amount = "100.00",
                vendor_invoice_no = "INV-300",
                memo = "charge"
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.PayableCharge, charge.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var creditMemo = await documents.CreateDraftAsync(PropertyManagementCodes.PayableCreditMemo, Payload(new
            {
                party_id = vendor.Id,
                property_id = property.Id,
                charge_type_id = repairType.Id,
                credited_on_utc = "2026-03-07",
                amount = "60.00",
                memo = "credit"
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.PayableCreditMemo, creditMemo.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var apply = await documents.CreateDraftAsync(PropertyManagementCodes.PayableApply, Payload(new
            {
                credit_document_id = creditMemo.Id,
                charge_document_id = charge.Id,
                applied_on_utc = "2026-03-07",
                amount = "40.00",
                memo = "apply"
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.PayableApply, apply.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var itemDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.PayableItem}");
            var chargeOutstanding = await GetNetAmountForItemAsync(opregRead, policy.PayablesOpenItemsOperationalRegisterId, itemDimId, charge.Id);
            var creditNet = await GetNetAmountForItemAsync(opregRead, policy.PayablesOpenItemsOperationalRegisterId, itemDimId, creditMemo.Id);

            chargeOutstanding.Should().Be(60m);
            creditNet.Should().Be(-20m);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    private static async Task<decimal> GetNetAmountForItemAsync(
        IOperationalRegisterReadService opregRead,
        Guid registerId,
        Guid itemDimensionId,
        Guid itemId)
    {
        var page = await opregRead.GetMovementsPageAsync(
            new NGB.OperationalRegisters.Contracts.OperationalRegisterMovementsPageRequest(
                RegisterId: registerId,
                FromInclusive: new DateOnly(2026, 3, 1),
                ToInclusive: new DateOnly(2026, 4, 1),
                Dimensions: [new DimensionValue(itemDimensionId, itemId)],
                PageSize: 500),
            CancellationToken.None);

        var net = 0m;
        foreach (var l in page.Lines)
        {
            if (!l.Values.TryGetValue("amount", out var v))
                continue;

            net += l.IsStorno ? -v : v;
        }

        return net;
    }

    private static RecordPayload Payload(object obj)
    {
        var el = JsonSerializer.SerializeToElement(obj);
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
