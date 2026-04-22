using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Core.Dimensions;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Runtime;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmPayableCreditMemo_Posting_P0Tests(PmIntegrationFixture fixture) : IAsyncLifetime
{
    public async Task InitializeAsync() => await fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PostAsync_CreatesStandaloneVendorCredit_AndWritesNegativePayablesOpenItem()
    {
        var factory = new PmApiFactory(fixture);
        try
        {
            using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
                        var opregRead = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var policyReader = scope.ServiceProvider.GetRequiredService<IPropertyManagementAccountingPolicyReader>();

            var setupResult = await setup.EnsureDefaultsAsync(CancellationToken.None);
            var policy = await policyReader.GetRequiredAsync(CancellationToken.None);

            var vendor = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new
            {
                display = "Best Vendor LLC",
                is_tenant = false,
                is_vendor = true
            }), CancellationToken.None);

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

            var creditMemo = await documents.CreateDraftAsync(PropertyManagementCodes.PayableCreditMemo, Payload(new
            {
                party_id = vendor.Id,
                property_id = property.Id,
                charge_type_id = repairType.Id,
                credited_on_utc = "2026-03-07",
                amount = 87.50m,
                memo = "Vendor credit memo"
            }), CancellationToken.None);

            var posted = await documents.PostAsync(PropertyManagementCodes.PayableCreditMemo, creditMemo.Id, CancellationToken.None);
            posted.Status.Should().Be(DocumentStatus.Posted);
            posted.Payload.Fields!["display"].GetString().Should().StartWith("Payable Credit Memo ");

            await uow.BeginTransactionAsync(CancellationToken.None);
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var repairDebitAccountId = await uow.Connection.QuerySingleAsync<Guid>(
                new CommandDefinition(
                    "SELECT debit_account_id FROM cat_pm_payable_charge_type WHERE catalog_id = @catalog_id;",
                    new { catalog_id = repairType.Id },
                    uow.Transaction,
                    cancellationToken: CancellationToken.None));

            const string sql = """
SELECT period AS Period,
       debit_account_id AS DebitAccountId,
       credit_account_id AS CreditAccountId,
       amount AS Amount
FROM accounting_register_main
WHERE document_id = @document_id AND is_storno = FALSE;
""";

            var entry = await uow.Connection.QuerySingleAsync<AccountingEntryRow>(
                new CommandDefinition(sql, new { document_id = creditMemo.Id }, uow.Transaction, cancellationToken: CancellationToken.None));

            entry.DebitAccountId.Should().Be(policy.AccountsPayableVendorsAccountId);
            entry.CreditAccountId.Should().Be(repairDebitAccountId);
            entry.Amount.Should().Be(87.50m);
            entry.Period.Should().Be(new DateTime(2026, 3, 7, 0, 0, 0, DateTimeKind.Utc));

            var movements = await opregRead.GetMovementsPageAsync(
                new OperationalRegisterMovementsPageRequest(
                    RegisterId: setupResult.PayablesOpenItemsOperationalRegisterId,
                    FromInclusive: new DateOnly(2026, 3, 1),
                    ToInclusive: new DateOnly(2026, 4, 1),
                    DocumentId: creditMemo.Id,
                    PageSize: 50),
                CancellationToken.None);

            movements.Lines.Should().HaveCount(1);
            movements.Lines[0].IsStorno.Should().BeFalse();
            movements.Lines[0].OccurredAtUtc.Should().Be(new DateTime(2026, 3, 7, 0, 0, 0, DateTimeKind.Utc));
            movements.Lines[0].Values["amount"].Should().Be(-87.50m);

            var itemDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.PayableItem}");
            var creditNet = await GetNetAmountForItemAsync(opregRead, policy.PayablesOpenItemsOperationalRegisterId, itemDimId, creditMemo.Id);
            creditNet.Should().Be(-87.50m);

            await uow.RollbackAsync(CancellationToken.None);
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
            new OperationalRegisterMovementsPageRequest(
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

    private sealed record AccountingEntryRow(DateTime Period, Guid DebitAccountId, Guid CreditAccountId, decimal Amount);

    private static RecordPayload Payload(object fields)
    {
        var el = JsonSerializer.SerializeToElement(fields);
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
