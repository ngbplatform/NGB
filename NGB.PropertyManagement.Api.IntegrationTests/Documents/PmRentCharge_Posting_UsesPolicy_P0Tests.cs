using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Runtime;
using NGB.Runtime.Accounts;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmRentCharge_Posting_UsesPolicy_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmRentCharge_Posting_UsesPolicy_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PostAsync_UsesAccountingPolicyAccounts_AndWritesOperationalRegisterMovement()
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
            var opregRead = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            // Ensure policy + accounts + OR exist.
            var setupResult = await setup.EnsureDefaultsAsync(CancellationToken.None);

            // Create an alternative income account and update policy to use it.
            var altIncomeCode = $"pm-test-income-{Guid.CreateVersion7():N}";
            var altIncomeId = await coaManagement.CreateAsync(
                new CreateAccountRequest(
                    Code: altIncomeCode,
                    Name: $"Rental Income - Alt ({altIncomeCode})",
                    Type: AccountType.Income,
                    StatementSection: StatementSection.Income,
                    IsContra: false,
                    NegativeBalancePolicy: null,
                    IsActive: true,
                    DimensionRules:
                    [
                        new AccountDimensionRuleRequest(PropertyManagementCodes.Party, IsRequired: true, Ordinal: 1),
                        new AccountDimensionRuleRequest(PropertyManagementCodes.Property, IsRequired: true, Ordinal: 2),
                        new AccountDimensionRuleRequest(PropertyManagementCodes.Lease, IsRequired: true, Ordinal: 3)
                    ]),
                CancellationToken.None);

            var policy = await catalogs.GetByIdAsync(PropertyManagementCodes.AccountingPolicy, setupResult.AccountingPolicyCatalogId, CancellationToken.None);

            static JsonElement J<T>(T v) => JsonSerializer.SerializeToElement(v);
            var policyFields = policy.Payload.Fields!;

            var updatedPolicyPayload = new RecordPayload(
                Fields: new Dictionary<string, JsonElement>
                {
                    ["display"] = J(policy.Display ?? "PM Policy"),
                    ["cash_account_id"] = policyFields["cash_account_id"],
                    ["ar_tenants_account_id"] = policyFields["ar_tenants_account_id"],
                    ["ap_vendors_account_id"] = policyFields["ap_vendors_account_id"],
                    ["rent_income_account_id"] = J(altIncomeId),
                    ["late_fee_income_account_id"] = policyFields["late_fee_income_account_id"],
                    ["tenant_balances_register_id"] = policyFields["tenant_balances_register_id"],
                    ["receivables_open_items_register_id"] = policyFields["receivables_open_items_register_id"],
                    ["payables_open_items_register_id"] = policyFields["payables_open_items_register_id"],
                },
                Parts: null);

            await catalogs.UpdateAsync(PropertyManagementCodes.AccountingPolicy, policy.Id, updatedPolicyPayload, CancellationToken.None);

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

            // Rent charge
            var rentCharge = await documents.CreateDraftAsync(PropertyManagementCodes.RentCharge, Payload(new
            {
                display = "RC-1",
                lease_id = lease.Id,
                period_from_utc = "2026-02-01",
                period_to_utc = "2026-02-28",
                due_on_utc = "2026-02-05",
                amount = "123.45",
                memo = "m"
            }), CancellationToken.None);

            var posted = await documents.PostAsync(PropertyManagementCodes.RentCharge, rentCharge.Id, CancellationToken.None);
            posted.Status.Should().Be(DocumentStatus.Posted);

            // Verify accounting entry uses policy accounts.
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
                new CommandDefinition(sql, new { document_id = rentCharge.Id }, uow.Transaction, cancellationToken: CancellationToken.None));

            entry.DebitAccountId.Should().Be(setupResult.AccountsReceivableTenantsAccountId);
            entry.CreditAccountId.Should().Be(altIncomeId);
            entry.Amount.Should().Be(123.45m);

            // OccurredAtUtc = due_on_utc at midnight UTC
            entry.Period.Should().Be(new DateTime(2026, 2, 5, 0, 0, 0, DateTimeKind.Utc));

            // Verify open-items OR movement
            // NOTE: OR reader contract expects month-start boundaries.
            var mv = await opregRead.GetMovementsPageAsync(
                new NGB.OperationalRegisters.Contracts.OperationalRegisterMovementsPageRequest(
                    RegisterId: setupResult.ReceivablesOpenItemsOperationalRegisterId,
                    FromInclusive: new DateOnly(2026, 2, 1),
                    ToInclusive: new DateOnly(2026, 3, 1),
                    DocumentId: rentCharge.Id,
                    PageSize: 50),
                CancellationToken.None);

            mv.Lines.Should().HaveCount(1);
            mv.Lines[0].IsStorno.Should().BeFalse();
            mv.Lines[0].OccurredAtUtc.Should().Be(new DateTime(2026, 2, 5, 0, 0, 0, DateTimeKind.Utc));
            mv.Lines[0].Values.Should().ContainKey("amount");
            mv.Lines[0].Values["amount"].Should().Be(123.45m);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task PostAsync_UsesAccountingPolicyOperationalRegisterId()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

            await using var scope = factory.Services.CreateAsyncScope();

            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
            var opregManagement = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
            var opregRead = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();

            // Ensure policy + default OR exist.
            var setupResult = await setup.EnsureDefaultsAsync(CancellationToken.None);

            // Create an alternative OR and switch policy to it.
            var altCode = $"pm-test-open-items-{Guid.CreateVersion7():N}";
            var altRegId = await opregManagement.UpsertAsync(altCode, "Receivables Open Items - Alt", CancellationToken.None);

            await opregManagement.ReplaceResourcesAsync(
                altRegId,
                [new OperationalRegisterResourceDefinition("amount", "Amount", 1)],
                CancellationToken.None);

            await opregManagement.ReplaceDimensionRulesAsync(
                altRegId,
                [
                    new OperationalRegisterDimensionRule(
                        DimensionId: DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}"),
                        DimensionCode: PropertyManagementCodes.Party,
                        Ordinal: 1,
                        IsRequired: true),
                    new OperationalRegisterDimensionRule(
                        DimensionId: DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}"),
                        DimensionCode: PropertyManagementCodes.Property,
                        Ordinal: 2,
                        IsRequired: true),
                    new OperationalRegisterDimensionRule(
                        DimensionId: DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Lease}"),
                        DimensionCode: PropertyManagementCodes.Lease,
                        Ordinal: 3,
                        IsRequired: true),
                    new OperationalRegisterDimensionRule(
                        DimensionId: DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.ReceivableItem}"),
                        DimensionCode: PropertyManagementCodes.ReceivableItem,
                        Ordinal: 4,
                        IsRequired: true)
                ],
                CancellationToken.None);

            var policy = await catalogs.GetByIdAsync(PropertyManagementCodes.AccountingPolicy, setupResult.AccountingPolicyCatalogId, CancellationToken.None);

            static JsonElement J<T>(T v) => JsonSerializer.SerializeToElement(v);
            var policyFields = policy.Payload.Fields!;

            var updatedPolicyPayload = new RecordPayload(
                Fields: new Dictionary<string, JsonElement>
                {
                    ["display"] = J(policy.Display ?? "PM Policy"),
                    ["cash_account_id"] = policyFields["cash_account_id"],
                    ["ar_tenants_account_id"] = policyFields["ar_tenants_account_id"],
                    ["ap_vendors_account_id"] = policyFields["ap_vendors_account_id"],
                    ["rent_income_account_id"] = policyFields["rent_income_account_id"],
                    ["late_fee_income_account_id"] = policyFields["late_fee_income_account_id"],
                    ["tenant_balances_register_id"] = policyFields["tenant_balances_register_id"],
                    ["receivables_open_items_register_id"] = J(altRegId),
                    ["payables_open_items_register_id"] = policyFields["payables_open_items_register_id"],
                },
                Parts: null);

            await catalogs.UpdateAsync(PropertyManagementCodes.AccountingPolicy, policy.Id, updatedPolicyPayload, CancellationToken.None);

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

            // Rent charge
            var rentCharge = await documents.CreateDraftAsync(PropertyManagementCodes.RentCharge, Payload(new
            {
                display = "RC-1",
                lease_id = lease.Id,
                period_from_utc = "2026-02-01",
                period_to_utc = "2026-02-28",
                due_on_utc = "2026-02-05",
                amount = "123.45",
                memo = "m"
            }), CancellationToken.None);

            var posted = await documents.PostAsync(PropertyManagementCodes.RentCharge, rentCharge.Id, CancellationToken.None);
            posted.Status.Should().Be(DocumentStatus.Posted);

            // Verify OR movement is written to the policy-selected register.
            var mvAlt = await opregRead.GetMovementsPageAsync(
                new OperationalRegisterMovementsPageRequest(
                    RegisterId: altRegId,
                    FromInclusive: new DateOnly(2026, 2, 1),
                    ToInclusive: new DateOnly(2026, 3, 1),
                    DocumentId: rentCharge.Id,
                    PageSize: 50),
                CancellationToken.None);

            mvAlt.Lines.Should().HaveCount(1);
            mvAlt.Lines[0].Values["amount"].Should().Be(123.45m);

            // And NOT to the default OR.
            var mvDefault = await opregRead.GetMovementsPageAsync(
                new OperationalRegisterMovementsPageRequest(
                    RegisterId: setupResult.ReceivablesOpenItemsOperationalRegisterId,
                    FromInclusive: new DateOnly(2026, 2, 1),
                    ToInclusive: new DateOnly(2026, 3, 1),
                    DocumentId: rentCharge.Id,
                    PageSize: 50),
                CancellationToken.None);

            mvDefault.Lines.Should().BeEmpty();
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    // Dapper-friendly POCO: accounting_register_main.entry_id is BIGINT, not UUID.
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
