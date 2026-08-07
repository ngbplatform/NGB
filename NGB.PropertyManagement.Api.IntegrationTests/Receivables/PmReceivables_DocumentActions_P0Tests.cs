using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Runtime;
using NGB.PropertyManagement.Runtime.DocumentActions;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Receivables;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReceivables_DocumentActions_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmReceivables_DocumentActions_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ApplyAction_DependsOnOutstandingOrCredit()
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

            var c1 = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                display = "RC-1",
                lease_id = lease.Id,
                charge_type_id = rentType.Id,
                due_on_utc = "2026-02-05",
                amount = "100.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, c1.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var c2 = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                display = "RC-2",
                lease_id = lease.Id,
                charge_type_id = rentType.Id,
                due_on_utc = "2026-02-10",
                amount = "20.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, c2.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var p1 = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                display = "RP-1",
                lease_id = lease.Id,
                received_on_utc = "2026-02-07",
                amount = "120.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, p1.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            // Before any applications: outstanding/credit exist => canApply is true.
            (await client.GetDocumentActionAsync(PropertyManagementCodes.ReceivableCharge, c1.Id, PropertyManagementDocumentActionCodes.OpenReceivablesReconciliation.Value)).IsAllowed.Should().BeTrue();
            (await client.GetDocumentActionAsync(PropertyManagementCodes.ReceivablePayment, p1.Id, PropertyManagementDocumentActionCodes.OpenReceivablesReconciliation.Value)).IsAllowed.Should().BeTrue();

            var a1 = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableApply, Payload(new
            {
                display = "RA-1",
                credit_document_id = p1.Id,
                charge_document_id = c1.Id,
                applied_on_utc = "2026-02-07",
                amount = "70.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivableApply, a1.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            // Partial apply: still outstanding and credit.
            (await client.GetDocumentActionAsync(PropertyManagementCodes.ReceivableCharge, c1.Id, PropertyManagementDocumentActionCodes.OpenReceivablesReconciliation.Value)).IsAllowed.Should().BeTrue();
            (await client.GetDocumentActionAsync(PropertyManagementCodes.ReceivablePayment, p1.Id, PropertyManagementDocumentActionCodes.OpenReceivablesReconciliation.Value)).IsAllowed.Should().BeTrue();

            // Close everything: charge1(30) + charge2(20) = remaining credit 50.
            var a2 = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableApply, Payload(new
            {
                display = "RA-2",
                credit_document_id = p1.Id,
                charge_document_id = c1.Id,
                applied_on_utc = "2026-02-08",
                amount = "30.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivableApply, a2.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var a3 = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableApply, Payload(new
            {
                display = "RA-3",
                credit_document_id = p1.Id,
                charge_document_id = c2.Id,
                applied_on_utc = "2026-02-08",
                amount = "20.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivableApply, a3.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var c1Action = await client.GetDocumentActionAsync(PropertyManagementCodes.ReceivableCharge, c1.Id, PropertyManagementDocumentActionCodes.OpenReceivablesReconciliation.Value);
            c1Action.IsAllowed.Should().BeFalse();
            c1Action.DisabledReasons.Single().Code.Should().Be("pm.receivables.apply.no_outstanding");

            var c2Action = await client.GetDocumentActionAsync(PropertyManagementCodes.ReceivableCharge, c2.Id, PropertyManagementDocumentActionCodes.OpenReceivablesReconciliation.Value);
            c2Action.IsAllowed.Should().BeFalse();
            c2Action.DisabledReasons.Single().Code.Should().Be("pm.receivables.apply.no_outstanding");

            var paymentAction = await client.GetDocumentActionAsync(PropertyManagementCodes.ReceivablePayment, p1.Id, PropertyManagementDocumentActionCodes.OpenReceivablesReconciliation.Value);
            paymentAction.IsAllowed.Should().BeFalse();
            paymentAction.DisabledReasons.Single().Code.Should().Be("pm.receivables.apply.no_credit");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task ApplyAction_ForRentCharge_IsAllowedWhenOutstandingExists()
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

            var property = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
            {
                kind = "Unit",
                parent_property_id = building.Id,
                unit_no = "201"
            }), CancellationToken.None);

            var lease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
            {
                property_id = property.Id,
                start_on_utc = "2026-02-01",
                rent_amount = "1000.00"
            }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);

            var rentCharge = await documents.CreateDraftAsync(PropertyManagementCodes.RentCharge, Payload(new
            {
                lease_id = lease.Id,
                period_from_utc = "2026-02-01",
                period_to_utc = "2026-02-28",
                due_on_utc = "2026-02-05",
                amount = "100.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.RentCharge, rentCharge.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var action = await client.GetDocumentActionAsync(PropertyManagementCodes.RentCharge, rentCharge.Id, PropertyManagementDocumentActionCodes.OpenReceivablesReconciliation.Value);
            action.IsAllowed.Should().BeTrue();
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
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
