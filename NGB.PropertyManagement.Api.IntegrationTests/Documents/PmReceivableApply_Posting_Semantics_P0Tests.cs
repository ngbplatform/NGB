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
public sealed class PmReceivableApply_Posting_Semantics_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmReceivableApply_Posting_Semantics_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

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

            // Minimal data: party + property + lease
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

            // Charge type: use the default seeded "Utility".
            var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
            var rentType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));

            // Two charges.
            var charge1 = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                display = "RC-1",
                lease_id = lease.Id,
                charge_type_id = rentType.Id,
                due_on_utc = "2026-02-05",
                amount = "100.00",
                memo = "m"
            }), CancellationToken.None);

            (await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge1.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var charge2 = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                display = "RC-2",
                lease_id = lease.Id,
                charge_type_id = rentType.Id,
                due_on_utc = "2026-02-06",
                amount = "80.00",
                memo = "m"
            }), CancellationToken.None);

            (await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge2.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            // Payment creates a credit open item.
            var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                display = "RP-1",
                lease_id = lease.Id,
                received_on_utc = "2026-02-07",
                amount = "100.00",
                memo = "m"
            }), CancellationToken.None);

            (await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            // Apply: allocate 60 from payment credit to charge1.
            var apply1 = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableApply, Payload(new
            {
                display = "RA-1",
                credit_document_id = payment.Id,
                charge_document_id = charge1.Id,
                applied_on_utc = "2026-02-07",
                amount = "60.00",
                memo = "m"
            }), CancellationToken.None);

            (await documents.PostAsync(PropertyManagementCodes.ReceivableApply, apply1.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            // Verify the apply document wrote two movements.
            var applyMovements = await opregRead.GetMovementsPageAsync(
                new NGB.OperationalRegisters.Contracts.OperationalRegisterMovementsPageRequest(
                    RegisterId: policy.ReceivablesOpenItemsOperationalRegisterId,
                    FromInclusive: new DateOnly(2026, 2, 1),
                    ToInclusive: new DateOnly(2026, 3, 1),
                    DocumentId: apply1.Id,
                    PageSize: 50),
                CancellationToken.None);

            applyMovements.Lines.Should().HaveCount(2);
            applyMovements.Lines.All(x => x.IsStorno == false).Should().BeTrue();
            applyMovements.Lines.Select(x => x.Values["amount"]).Should().BeEquivalentTo([-60m, 60m]);

            // Verify item outstanding balances.
            var itemDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.ReceivableItem}");

            var charge1Outstanding = await GetNetAmountForItemAsync(opregRead, policy.ReceivablesOpenItemsOperationalRegisterId, itemDimId, charge1.Id);
            charge1Outstanding.Should().Be(40m);

            var paymentNet = await GetNetAmountForItemAsync(opregRead, policy.ReceivablesOpenItemsOperationalRegisterId, itemDimId, payment.Id);
            paymentNet.Should().Be(-40m);

            // Cannot over-apply charge outstanding (only 40 remaining).
            var overCharge = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableApply, Payload(new
            {
                display = "RA-OVER-CHARGE",
                credit_document_id = payment.Id,
                charge_document_id = charge1.Id,
                applied_on_utc = "2026-02-07",
                amount = "50.00",
            }), CancellationToken.None);

            var actOverCharge = async () => await documents.PostAsync(PropertyManagementCodes.ReceivableApply, overCharge.Id, CancellationToken.None);
            await actOverCharge.Should().ThrowAsync<ReceivableApplyValidationException>();

            // Cannot apply beyond available credit (only 40 credit remains).
            var overCredit = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableApply, Payload(new
            {
                display = "RA-OVER-CREDIT",
                credit_document_id = payment.Id,
                charge_document_id = charge2.Id,
                applied_on_utc = "2026-02-07",
                amount = "50.00",
            }), CancellationToken.None);

            var actOverCredit = async () => await documents.PostAsync(PropertyManagementCodes.ReceivableApply, overCredit.Id, CancellationToken.None);
            await actOverCredit.Should().ThrowAsync<ReceivableApplyValidationException>();
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
                FromInclusive: new DateOnly(2026, 2, 1),
                ToInclusive: new DateOnly(2026, 3, 1),
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
