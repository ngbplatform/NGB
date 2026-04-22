using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Core.Dimensions;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Runtime;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReceivableApply_VariantB_History_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmReceivableApply_VariantB_History_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PostUnpostPost_KeepsAppendOnlyLifecycleAndOperationalRegisterHistory()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var opregRead = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();

            var seeded = await SeedDraftReceivableApplyAsync(scope.ServiceProvider, appliedAmount: "60.00");

            (await documents.PostAsync(PropertyManagementCodes.ReceivableApply, seeded.Apply.Id, CancellationToken.None))
                .Status.Should().Be(DocumentStatus.Posted);

            (await documents.UnpostAsync(PropertyManagementCodes.ReceivableApply, seeded.Apply.Id, CancellationToken.None))
                .Status.Should().Be(DocumentStatus.Draft);

            (await documents.PostAsync(PropertyManagementCodes.ReceivableApply, seeded.Apply.Id, CancellationToken.None))
                .Status.Should().Be(DocumentStatus.Posted);

            var current = await documents.GetByIdAsync(PropertyManagementCodes.ReceivableApply, seeded.Apply.Id, CancellationToken.None);
            current.Status.Should().Be(DocumentStatus.Posted);

            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var documentHistory = await uow.Connection.QuerySingleAsync<int>(
                new CommandDefinition(
                    "SELECT COUNT(*)::int FROM platform_document_operation_history WHERE document_id = @document_id;",
                    new { document_id = seeded.Apply.Id },
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));

            var accountingHistory = await uow.Connection.QuerySingleAsync<int>(
                new CommandDefinition(
                    "SELECT COUNT(*)::int FROM accounting_posting_log_history WHERE document_id = @document_id;",
                    new { document_id = seeded.Apply.Id },
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));

            var opregHistory = await uow.Connection.QuerySingleAsync<int>(
                new CommandDefinition(
                    "SELECT COUNT(*)::int FROM operational_register_write_log_history WHERE document_id = @document_id;",
                    new { document_id = seeded.Apply.Id },
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));

            documentHistory.Should().Be(6);
            accountingHistory.Should().Be(0);
            opregHistory.Should().Be(6);

            const string accountingSql = "SELECT COUNT(*)::int FROM accounting_register_main WHERE document_id = @document_id;";
            var accountingRows = await uow.Connection.QuerySingleAsync<int>(
                new CommandDefinition(accountingSql, new { document_id = seeded.Apply.Id }, uow.Transaction, cancellationToken: CancellationToken.None));
            accountingRows.Should().Be(0);

            var movements = await opregRead.GetMovementsPageAsync(
                new OperationalRegisterMovementsPageRequest(
                    RegisterId: seeded.RegisterId,
                    FromInclusive: new DateOnly(2026, 2, 1),
                    ToInclusive: new DateOnly(2026, 3, 1),
                    DocumentId: seeded.Apply.Id,
                    PageSize: 50),
                CancellationToken.None);

            movements.Lines.Should().HaveCount(6);
            movements.Lines.Count(x => x.IsStorno).Should().Be(2);
            movements.Lines.Count(x => !x.IsStorno).Should().Be(4);

            var itemDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.ReceivableItem}");
            var chargeOutstanding = await GetNetAmountForItemAsync(opregRead, seeded.RegisterId, itemDimId, seeded.Charge.Id);
            chargeOutstanding.Should().Be(40m);

            var paymentNet = await GetNetAmountForItemAsync(opregRead, seeded.RegisterId, itemDimId, seeded.Payment.Id);
            paymentNet.Should().Be(-40m);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    private static async Task<(DocumentDto Apply, DocumentDto Payment, DocumentDto Charge, Guid RegisterId)> SeedDraftReceivableApplyAsync(IServiceProvider services, string appliedAmount)
    {
        var setup = services.GetRequiredService<IPropertyManagementSetupService>();
        var catalogs = services.GetRequiredService<ICatalogService>();
        var documents = services.GetRequiredService<IDocumentService>();

        var setupResult = await setup.EnsureDefaultsAsync(CancellationToken.None);

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

        var apply = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableApply, Payload(new
        {
            credit_document_id = payment.Id,
            charge_document_id = charge.Id,
            applied_on_utc = "2026-02-07",
            amount = appliedAmount,
            memo = "Apply"
        }), CancellationToken.None);

        return (apply, payment, charge, setupResult.ReceivablesOpenItemsOperationalRegisterId);
    }

    private static async Task<decimal> GetNetAmountForItemAsync(
        IOperationalRegisterReadService opregRead,
        Guid registerId,
        Guid itemDimensionId,
        Guid itemId)
    {
        var page = await opregRead.GetMovementsPageAsync(
            new OperationalRegisterMovementsPageRequest(
                RegisterId: registerId,
                FromInclusive: new DateOnly(2026, 2, 1),
                ToInclusive: new DateOnly(2026, 3, 1),
                Dimensions: [new DimensionValue(itemDimensionId, itemId)],
                PageSize: 500),
            CancellationToken.None);

        var net = 0m;
        foreach (var line in page.Lines)
        {
            if (!line.Values.TryGetValue("amount", out var value))
                continue;

            net += line.IsStorno ? -value : value;
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
        await factory.DisposeAsync();
        factory.Dispose();
    }
}
