using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.PropertyManagement.Runtime;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmPayableCharge_Posting_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmPayableCharge_Posting_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PostAsync_UsesPolicyAndChargeType_AndWritesPayablesOpenItem()
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
            var readers = scope.ServiceProvider.GetRequiredService<IPropertyManagementDocumentReaders>();

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
                display = "Main Building",
                address_line1 = "1 Demo Way",
                city = "Hoboken",
                state = "NJ",
                zip = "07030"
            }), CancellationToken.None);

            var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.PayableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
            var repairType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Repair", StringComparison.OrdinalIgnoreCase));

            await uow.BeginTransactionAsync(CancellationToken.None);
            var repairTypeHead = await readers.ReadPayableChargeTypeHeadAsync(repairType.Id, CancellationToken.None);
            await uow.RollbackAsync(CancellationToken.None);

            var payableCharge = await documents.CreateDraftAsync(PropertyManagementCodes.PayableCharge, Payload(new
            {
                party_id = vendor.Id,
                property_id = property.Id,
                charge_type_id = repairType.Id,
                due_on_utc = "2026-03-05",
                amount = "245.75",
                vendor_invoice_no = "INV-100",
                memo = "Repair bill"
            }), CancellationToken.None);

            var posted = await documents.PostAsync(PropertyManagementCodes.PayableCharge, payableCharge.Id, CancellationToken.None);
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
                new CommandDefinition(sql, new { document_id = payableCharge.Id }, uow.Transaction, cancellationToken: CancellationToken.None));

            entry.DebitAccountId.Should().Be(repairTypeHead.DebitAccountId!.Value);
            entry.CreditAccountId.Should().Be(policy.AccountsPayableVendorsAccountId);
            entry.Amount.Should().Be(245.75m);
            entry.Period.Should().Be(new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc));

            var movements = await opregRead.GetMovementsPageAsync(
                new OperationalRegisterMovementsPageRequest(
                    RegisterId: setupResult.PayablesOpenItemsOperationalRegisterId,
                    FromInclusive: new DateOnly(2026, 3, 1),
                    ToInclusive: new DateOnly(2026, 4, 1),
                    DocumentId: payableCharge.Id,
                    PageSize: 50),
                CancellationToken.None);

            movements.Lines.Should().HaveCount(1);
            movements.Lines[0].IsStorno.Should().BeFalse();
            movements.Lines[0].OccurredAtUtc.Should().Be(new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc));
            movements.Lines[0].Values["amount"].Should().Be(245.75m);

            await uow.RollbackAsync(CancellationToken.None);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
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
