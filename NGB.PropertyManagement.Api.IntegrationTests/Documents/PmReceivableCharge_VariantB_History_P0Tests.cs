using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Runtime;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReceivableCharge_VariantB_History_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmReceivableCharge_VariantB_History_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PostUnpostPost_KeepsAppendOnlyLifecycleAndSubsystemHistory()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var opregRead = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();

            var seeded = await SeedDraftReceivableChargeAsync(scope.ServiceProvider);

            (await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, seeded.ReceivableCharge.Id, CancellationToken.None))
                .Status.Should().Be(DocumentStatus.Posted);

            (await documents.UnpostAsync(PropertyManagementCodes.ReceivableCharge, seeded.ReceivableCharge.Id, CancellationToken.None))
                .Status.Should().Be(DocumentStatus.Draft);

            (await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, seeded.ReceivableCharge.Id, CancellationToken.None))
                .Status.Should().Be(DocumentStatus.Posted);

            var current = await documents.GetByIdAsync(PropertyManagementCodes.ReceivableCharge, seeded.ReceivableCharge.Id, CancellationToken.None);
            current.Status.Should().Be(DocumentStatus.Posted);

            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var documentHistory = await uow.Connection.QuerySingleAsync<int>(
                new CommandDefinition(
                    "SELECT COUNT(*)::int FROM platform_document_operation_history WHERE document_id = @document_id;",
                    new { document_id = seeded.ReceivableCharge.Id },
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));

            var accountingHistory = await uow.Connection.QuerySingleAsync<int>(
                new CommandDefinition(
                    "SELECT COUNT(*)::int FROM accounting_posting_log_history WHERE document_id = @document_id;",
                    new { document_id = seeded.ReceivableCharge.Id },
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));

            var opregHistory = await uow.Connection.QuerySingleAsync<int>(
                new CommandDefinition(
                    "SELECT COUNT(*)::int FROM operational_register_write_log_history WHERE document_id = @document_id;",
                    new { document_id = seeded.ReceivableCharge.Id },
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));

            documentHistory.Should().Be(6);
            accountingHistory.Should().Be(6);
            opregHistory.Should().Be(6);

            const string accountingSql = """
SELECT amount AS Amount, is_storno AS IsStorno
FROM accounting_register_main
WHERE document_id = @document_id
ORDER BY entry_id;
""";

            var accountingRows = (await uow.Connection.QueryAsync<AccountingEntryRow>(
                new CommandDefinition(accountingSql, new { document_id = seeded.ReceivableCharge.Id }, uow.Transaction, cancellationToken: CancellationToken.None)))
                .AsList();

            accountingRows.Should().HaveCount(3);
            accountingRows.Sum(x => x.IsStorno ? -x.Amount : x.Amount).Should().Be(123.45m);

            var movements = await opregRead.GetMovementsPageAsync(
                new OperationalRegisterMovementsPageRequest(
                    RegisterId: seeded.RegisterId,
                    FromInclusive: new DateOnly(2026, 2, 1),
                    ToInclusive: new DateOnly(2026, 3, 1),
                    DocumentId: seeded.ReceivableCharge.Id,
                    PageSize: 50),
                CancellationToken.None);

            movements.Lines.Should().HaveCount(3);
            movements.Lines.Sum(x => x.IsStorno ? -Convert.ToDecimal(x.Values["amount"]) : Convert.ToDecimal(x.Values["amount"])).Should().Be(123.45m);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    private static async Task<(DocumentDto ReceivableCharge, Guid RegisterId)> SeedDraftReceivableChargeAsync(IServiceProvider services)
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

        var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
        var rentType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));

        var receivableCharge = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
        {
            lease_id = lease.Id,
            charge_type_id = rentType.Id,
            due_on_utc = "2026-02-05",
            amount = "123.45",
            memo = "Charge"
        }), CancellationToken.None);

        return (receivableCharge, setupResult.ReceivablesOpenItemsOperationalRegisterId);
    }

    private static RecordPayload Payload(object obj, IReadOnlyDictionary<string, RecordPartPayload>? parts = null)
    {
        var el = JsonSerializer.SerializeToElement(obj);
        var dict = new Dictionary<string, JsonElement>();
        foreach (var p in el.EnumerateObject())
            dict[p.Name] = p.Value;
        return new RecordPayload(dict, parts);
    }

    private sealed record AccountingEntryRow(decimal Amount, bool IsStorno);

    private static async Task DisposeFactoryAsync(PmApiFactory factory)
    {
        await factory.DisposeAsync();
        factory.Dispose();
    }
}
