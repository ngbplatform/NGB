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
public sealed class PmPayablePayment_Posting_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmPayablePayment_Posting_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PostAsync_CreatesStandaloneVendorCredit_AndWritesNegativePayablesOpenItem()
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
            var policyReader = scope.ServiceProvider.GetRequiredService<IPropertyManagementAccountingPolicyReader>();
            var bankAccounts = scope.ServiceProvider.GetRequiredService<IPropertyManagementBankAccountReader>();

            var setupResult = await setup.EnsureDefaultsAsync(CancellationToken.None);
            var policy = await policyReader.GetRequiredAsync(CancellationToken.None);
            var defaultBankAccount = await bankAccounts.TryGetDefaultAsync(CancellationToken.None);
            defaultBankAccount.Should().NotBeNull();

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

            var payablePayment = await documents.CreateDraftAsync(PropertyManagementCodes.PayablePayment, Payload(new
            {
                party_id = vendor.Id,
                property_id = property.Id,
                bank_account_id = defaultBankAccount!.BankAccountId,
                paid_on_utc = "2026-03-06",
                amount = 100.25m,
                memo = "Vendor payment"
            }), CancellationToken.None);

            var posted = await documents.PostAsync(PropertyManagementCodes.PayablePayment, payablePayment.Id, CancellationToken.None);
            posted.Status.Should().Be(DocumentStatus.Posted);

            await uow.BeginTransactionAsync(CancellationToken.None);
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            const string sql = """
SELECT period AS Period,
       debit_account_id AS DebitAccountId,
       credit_account_id AS CreditAccountId,
       amount AS Amount
FROM accounting_register_main
WHERE document_id = @document_id AND is_storno = FALSE;
""";

            var entry = await uow.Connection.QuerySingleAsync<AccountingEntryRow>(
                new CommandDefinition(sql, new { document_id = payablePayment.Id }, uow.Transaction, cancellationToken: CancellationToken.None));

            entry.DebitAccountId.Should().Be(policy.AccountsPayableVendorsAccountId);
            entry.CreditAccountId.Should().Be(defaultBankAccount.GlAccountId);
            entry.Amount.Should().Be(100.25m);
            entry.Period.Should().Be(new DateTime(2026, 3, 6, 0, 0, 0, DateTimeKind.Utc));

            var movements = await opregRead.GetMovementsPageAsync(
                new OperationalRegisterMovementsPageRequest(
                    RegisterId: setupResult.PayablesOpenItemsOperationalRegisterId,
                    FromInclusive: new DateOnly(2026, 3, 1),
                    ToInclusive: new DateOnly(2026, 4, 1),
                    DocumentId: payablePayment.Id,
                    PageSize: 50),
                CancellationToken.None);

            movements.Lines.Should().HaveCount(1);
            movements.Lines[0].IsStorno.Should().BeFalse();
            movements.Lines[0].OccurredAtUtc.Should().Be(new DateTime(2026, 3, 6, 0, 0, 0, DateTimeKind.Utc));
            movements.Lines[0].Values["amount"].Should().Be(-100.25m);

            var itemDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.PayableItem}");
            var paymentNet = await GetNetAmountForItemAsync(opregRead, policy.PayablesOpenItemsOperationalRegisterId, itemDimId, payablePayment.Id);
            paymentNet.Should().Be(-100.25m);

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
