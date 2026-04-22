using System.Text.Json;
using Dapper;
using NGB.Accounting.Accounts;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Runtime;
using NGB.Runtime.Accounts;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReceivablePayment_Posting_CreatesCreditOpenItem_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmReceivablePayment_Posting_CreatesCreditOpenItem_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PostAsync_CreatesCashReceiptEntry_AndWritesNegativeOpenItemMovement()
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

            var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                display = "RP-1",
                lease_id = lease.Id,
                received_on_utc = "2026-02-07",
                amount = "50.00",
                memo = "m"
            }), CancellationToken.None);

            var posted = await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None);
            posted.Status.Should().Be(DocumentStatus.Posted);

            // Verify accounting entry (Dr Cash / Cr AR)
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            const string sql = """
SELECT
  entry_id AS EntryId,
  period AS Period,
  debit_account_id AS DebitAccountId,
  credit_account_id AS CreditAccountId,
  amount AS Amount
FROM accounting_register_main
WHERE document_id = @document_id AND is_storno = FALSE;
""";

            var entry = await uow.Connection.QuerySingleAsync<AccountingEntryRow>(
                new CommandDefinition(sql, new { document_id = payment.Id }, uow.Transaction, cancellationToken: CancellationToken.None));

            entry.DebitAccountId.Should().Be(setupResult.CashAccountId);
            entry.CreditAccountId.Should().Be(setupResult.AccountsReceivableTenantsAccountId);
            entry.Amount.Should().Be(50.00m);
            entry.Period.Should().Be(new DateTime(2026, 2, 7, 0, 0, 0, DateTimeKind.Utc));

            // Verify OR open-item movement: negative amount (credit)
            var mv = await opregRead.GetMovementsPageAsync(
                new NGB.OperationalRegisters.Contracts.OperationalRegisterMovementsPageRequest(
                    RegisterId: setupResult.ReceivablesOpenItemsOperationalRegisterId,
                    FromInclusive: new DateOnly(2026, 2, 1),
                    ToInclusive: new DateOnly(2026, 3, 1),
                    DocumentId: payment.Id,
                    PageSize: 50),
                CancellationToken.None);

            mv.Lines.Should().HaveCount(1);
            mv.Lines[0].IsStorno.Should().BeFalse();
            mv.Lines[0].OccurredAtUtc.Should().Be(new DateTime(2026, 2, 7, 0, 0, 0, DateTimeKind.Utc));
            mv.Lines[0].Values.Should().ContainKey("amount");
            mv.Lines[0].Values["amount"].Should().Be(-50.00m);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }


    [Fact]
    public async Task PostAsync_WhenBankAccountSelected_UsesBankAccountGlAccount()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
            var coaManagement = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await setup.EnsureDefaultsAsync(CancellationToken.None);

            var escrowCashId = await coaManagement.CreateAsync(
                new CreateAccountRequest(
                    Code: "1001",
                    Name: "Escrow Cash",
                    Type: AccountType.Asset,
                    StatementSection: StatementSection.Assets,
                    IsContra: false,
                    NegativeBalancePolicy: null,
                    IsActive: true,
                    DimensionRules: []),
                CancellationToken.None);

            var bankAccount = await catalogs.CreateAsync(PropertyManagementCodes.BankAccount, Payload(new
            {
                display = "Escrow Account",
                bank_name = "Demo Bank",
                account_name = "Escrow",
                last4 = "1001",
                gl_account_id = escrowCashId,
                is_default = false
            }), CancellationToken.None);

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
                rent_amount = "1000.00"
            }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);

            var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                lease_id = lease.Id,
                bank_account_id = bankAccount.Id,
                received_on_utc = "2026-02-07",
                amount = "50.00"
            }), CancellationToken.None);

            (await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            await uow.EnsureConnectionOpenAsync(CancellationToken.None);
            var entry = await uow.Connection.QuerySingleAsync<AccountingEntryRow>(
                new CommandDefinition(@"
SELECT debit_account_id AS DebitAccountId, credit_account_id AS CreditAccountId, amount AS Amount, period AS Period
FROM accounting_register_main
WHERE document_id = @document_id AND is_storno = FALSE;",
                parameters: new { document_id = payment.Id }, transaction: uow.Transaction, cancellationToken: CancellationToken.None));

            entry.DebitAccountId.Should().Be(escrowCashId);
            entry.CreditAccountId.Should().NotBe(escrowCashId);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task PostAsync_WithoutSelectedBankAccount_UsesDefaultBankAccountGlAccount()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
            var coaManagement = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var setupResult = await setup.EnsureDefaultsAsync(CancellationToken.None);

            var reserveCashId = await coaManagement.CreateAsync(
                new CreateAccountRequest(
                    Code: "1003",
                    Name: "Reserve Cash",
                    Type: AccountType.Asset,
                    StatementSection: StatementSection.Assets,
                    IsContra: false,
                    NegativeBalancePolicy: null,
                    IsActive: true,
                    DimensionRules: []),
                CancellationToken.None);

            await catalogs.UpdateAsync(PropertyManagementCodes.BankAccount, setupResult.DefaultBankAccountCatalogId, Payload(new
            {
                display = "Operating Account",
                bank_name = "Demo Bank",
                account_name = "Operating Account",
                last4 = "1000",
                gl_account_id = reserveCashId,
                is_default = true
            }), CancellationToken.None);

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
                rent_amount = "1000.00"
            }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);

            var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                lease_id = lease.Id,
                received_on_utc = "2026-02-07",
                amount = "50.00"
            }), CancellationToken.None);

            (await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            await uow.EnsureConnectionOpenAsync(CancellationToken.None);
            var entry = await uow.Connection.QuerySingleAsync<AccountingEntryRow>(
                new CommandDefinition(@"
SELECT debit_account_id AS DebitAccountId, credit_account_id AS CreditAccountId, amount AS Amount, period AS Period
FROM accounting_register_main
WHERE document_id = @document_id AND is_storno = FALSE;",
                parameters: new { document_id = payment.Id }, transaction: uow.Transaction, cancellationToken: CancellationToken.None));

            entry.DebitAccountId.Should().Be(reserveCashId);
            entry.DebitAccountId.Should().NotBe(setupResult.CashAccountId);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }


    private sealed class AccountingEntryRow
    {
        public long EntryId { get; init; }
        public DateTime Period { get; init; }
        public Guid DebitAccountId { get; init; }
        public Guid CreditAccountId { get; init; }
        public decimal Amount { get; init; }
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
