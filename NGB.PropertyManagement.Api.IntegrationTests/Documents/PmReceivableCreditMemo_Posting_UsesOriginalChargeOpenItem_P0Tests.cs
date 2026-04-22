using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Runtime;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReceivableCreditMemo_Posting_WritesStandaloneCreditItem_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmReceivableCreditMemo_Posting_WritesStandaloneCreditItem_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PostAsync_CreatesRevenueReversalEntry_AndWritesNegativeMovementToCreditMemoItem()
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
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var setupResult = await setup.EnsureDefaultsAsync(CancellationToken.None);

            var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "P" }), CancellationToken.None);
            var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new { kind = "Building", display = "A", address_line1 = "A", city = "Hoboken", state = "NJ", zip = "07030" }), CancellationToken.None);
            var property = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new { kind = "Unit", parent_property_id = building.Id, unit_no = "101" }), CancellationToken.None);
            var lease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new { property_id = property.Id, start_on_utc = "2026-02-01", rent_amount = "1000.00" }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);
            var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
            var chargeType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));

            var memo = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCreditMemo, Payload(new
            {
                lease_id = lease.Id,
                charge_type_id = chargeType.Id,
                credited_on_utc = "2026-02-08",
                amount = "25.00",
                memo = "Credit"
            }), CancellationToken.None);

            var posted = await documents.PostAsync(PropertyManagementCodes.ReceivableCreditMemo, memo.Id, CancellationToken.None);
            posted.Status.Should().Be(DocumentStatus.Posted);

            await uow.EnsureConnectionOpenAsync(CancellationToken.None);
            var expectedDebitAccountId = await uow.Connection.QuerySingleAsync<Guid>(
                new CommandDefinition(
                    "SELECT credit_account_id FROM cat_pm_receivable_charge_type WHERE catalog_id = @catalog_id;",
                    new { catalog_id = chargeType.Id },
                    uow.Transaction,
                    cancellationToken: CancellationToken.None));
            var entry = await uow.Connection.QuerySingleAsync<AccountingEntryRow>(
                new CommandDefinition(
                    "SELECT debit_account_id AS DebitAccountId, credit_account_id AS CreditAccountId, amount AS Amount, period AS Period FROM accounting_register_main WHERE document_id = @document_id AND is_storno = FALSE;",
                    new { document_id = memo.Id },
                    uow.Transaction,
                    cancellationToken: CancellationToken.None));

            entry.DebitAccountId.Should().Be(expectedDebitAccountId);
            entry.CreditAccountId.Should().Be(setupResult.AccountsReceivableTenantsAccountId);
            entry.Amount.Should().Be(25.00m);
            entry.Period.Should().Be(new DateTime(2026, 2, 8, 0, 0, 0, DateTimeKind.Utc));

            var mv = await opregRead.GetMovementsPageAsync(
                new NGB.OperationalRegisters.Contracts.OperationalRegisterMovementsPageRequest(
                    RegisterId: setupResult.ReceivablesOpenItemsOperationalRegisterId,
                    FromInclusive: new DateOnly(2026, 2, 1),
                    ToInclusive: new DateOnly(2026, 3, 1),
                    DocumentId: memo.Id,
                    PageSize: 50),
                CancellationToken.None);
            mv.Lines.Should().HaveCount(1);
            mv.Lines[0].Values["amount"].Should().Be(-25.00m);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    private sealed class AccountingEntryRow
    {
        public Guid DebitAccountId { get; init; }
        public Guid CreditAccountId { get; init; }
        public decimal Amount { get; init; }
        public DateTime Period { get; init; }
    }

    private static RecordPayload Payload(object obj, IReadOnlyDictionary<string, RecordPartPayload>? parts = null)
    {
        var el = JsonSerializer.SerializeToElement(obj);
        var dict = new Dictionary<string, JsonElement>();
        foreach (var p in el.EnumerateObject()) dict[p.Name] = p.Value;
        return new RecordPayload(dict, parts);
    }

    private static async Task DisposeFactoryAsync(PmApiFactory factory)
    {
        try { await factory.DisposeAsync(); } catch { }
    }
}
