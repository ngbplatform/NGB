using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Api.IntegrationTests.Support;
using NGB.PropertyManagement.Contracts.Payables;
using NGB.PropertyManagement.Payables;
using NGB.PropertyManagement.PostgreSql.Payables;
using NGB.PropertyManagement.Runtime;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Payables;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmPayablesReconciliation_Endpoint_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmPayablesReconciliation_Endpoint_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetReconciliation_WhenGlAndOpenItemsMatch_ReturnsZeroDiff_AndDefaultsToMovementMode()
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

            var vendor = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new
            {
                display = "Vendor One",
                is_vendor = true,
                is_tenant = false
            }), CancellationToken.None);

            var property = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
            {
                kind = "Building",
                address_line1 = "1 Payables Way",
                city = "Hoboken",
                state = "NJ",
                zip = "07030",
            }), CancellationToken.None);

            var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.PayableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
            var repairType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Repair", StringComparison.OrdinalIgnoreCase));

            var charge = await documents.CreateDraftAsync(PropertyManagementCodes.PayableCharge, Payload(new
            {
                party_id = vendor.Id,
                property_id = property.Id,
                charge_type_id = repairType.Id,
                due_on_utc = "2026-02-05",
                amount = "100.00",
                vendor_invoice_no = "INV-100",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.PayableCharge, charge.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var payment = await documents.CreateDraftAsync(PropertyManagementCodes.PayablePayment, Payload(new
            {
                party_id = vendor.Id,
                property_id = property.Id,
                paid_on_utc = "2026-02-07",
                amount = "60.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.PayablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var url = "/api/payables/reconciliation?fromMonthInclusive=2026-02-01&toMonthInclusive=2026-02-01";
            var report = await client.GetFromJsonAsync<PayablesReconciliationReport>(url);

            report.Should().NotBeNull();
            report!.Mode.Should().Be(PayablesReconciliationMode.Movement);
            report.RowCount.Should().Be(1);
            report.MismatchRowCount.Should().Be(0);
            report.TotalDiff.Should().Be(0m);

            report.Rows.Should().ContainSingle(r =>
                r.VendorId == vendor.Id &&
                r.VendorDisplay == vendor.Display &&
                r.PropertyId == property.Id &&
                r.PropertyDisplay == property.Display &&
                r.ApNet == 40m &&
                r.OpenItemsNet == 40m &&
                r.Diff == 0m &&
                r.RowKind == PayablesReconciliationRowKind.Matched &&
                r.HasDiff == false);

            var balanceReport = await client.GetFromJsonAsync<PayablesReconciliationReport>(
                "/api/payables/reconciliation?fromMonthInclusive=2026-02-01&toMonthInclusive=2026-02-01&mode=Balance");
            balanceReport.Should().NotBeNull();
            balanceReport!.Mode.Should().Be(PayablesReconciliationMode.Balance);
            balanceReport.TotalApNet.Should().Be(40m);
            balanceReport.TotalOpenItemsNet.Should().Be(40m);
            balanceReport.TotalDiff.Should().Be(0m);
            balanceReport.Rows.Should().ContainSingle(x =>
                x.RowKind == PayablesReconciliationRowKind.Matched && !x.HasDiff);

            var secondVendor = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new
            {
                display = "Vendor Two",
                is_vendor = true,
                is_tenant = false
            }), CancellationToken.None);
            var secondCharge = await documents.CreateDraftAsync(PropertyManagementCodes.PayableCharge, Payload(new
            {
                party_id = secondVendor.Id,
                property_id = property.Id,
                charge_type_id = repairType.Id,
                due_on_utc = "2026-02-08",
                amount = "25.00",
                vendor_invoice_no = "INV-200",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.PayableCharge, secondCharge.Id, CancellationToken.None))
                .Status.Should().Be(DocumentStatus.Posted);

            var firstPage = await client.GetFromJsonAsync<PayablesReconciliationReport>(url + "&limit=1");
            firstPage.Should().NotBeNull();
            firstPage!.Rows.Should().ContainSingle();
            firstPage.RowCount.Should().Be(2);
            firstPage.HasMore.Should().BeTrue();
            firstPage.NextCursor.Should().NotBeNullOrWhiteSpace();

            var nextPage = await client.GetFromJsonAsync<PayablesReconciliationReport>(
                url + "&limit=1&cursor=" + Uri.EscapeDataString(firstPage.NextCursor!));
            nextPage.Should().NotBeNull();
            nextPage!.Rows.Should().ContainSingle();
            nextPage.Rows[0].VendorId.Should().NotBe(firstPage.Rows[0].VendorId);
            nextPage.RowCount.Should().Be(firstPage.RowCount);
            nextPage.TotalApNet.Should().Be(firstPage.TotalApNet);
            nextPage.TotalOpenItemsNet.Should().Be(firstPage.TotalOpenItemsNet);
            nextPage.Offset.Should().Be(1);
            nextPage.HasMore.Should().BeFalse();
            nextPage.NextCursor.Should().BeNull();
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task GetReconciliation_WhenOpenItemsDriftExists_ReturnsNonZeroDiff_AndSupportsExplicitMovementMode()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            var setupResult = await setup.EnsureDefaultsAsync(CancellationToken.None);

            var vendor = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new
            {
                display = "Vendor One",
                is_vendor = true,
                is_tenant = false
            }), CancellationToken.None);

            var property = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
            {
                kind = "Building",
                address_line1 = "1 Payables Way",
                city = "Hoboken",
                state = "NJ",
                zip = "07030",
            }), CancellationToken.None);

            var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.PayableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
            var repairType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Repair", StringComparison.OrdinalIgnoreCase));

            var charge = await documents.CreateDraftAsync(PropertyManagementCodes.PayableCharge, Payload(new
            {
                party_id = vendor.Id,
                property_id = property.Id,
                charge_type_id = repairType.Id,
                due_on_utc = "2026-02-05",
                amount = "100.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.PayableCharge, charge.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var payment = await documents.CreateDraftAsync(PropertyManagementCodes.PayablePayment, Payload(new
            {
                party_id = vendor.Id,
                property_id = property.Id,
                paid_on_utc = "2026-02-07",
                amount = "60.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.PayablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            await using (var conn = new NpgsqlConnection(_fixture.ConnectionString))
            {
                await conn.OpenAsync();

                var tableCode = await conn.QuerySingleAsync<string>(
                    "SELECT table_code FROM operational_registers WHERE register_id = @Id::uuid;",
                    new { Id = setupResult.PayablesOpenItemsOperationalRegisterId });

                var table = $"opreg_{tableCode}__movements";

                var dimSetId = await conn.QuerySingleAsync<Guid>(
                    $"SELECT dimension_set_id FROM {table} WHERE document_id = @DocId::uuid LIMIT 1;",
                    new { DocId = charge.Id });

                await conn.ExecuteAsync(
                    $"INSERT INTO {table} (document_id, occurred_at_utc, dimension_set_id, amount) VALUES (@DocumentId::uuid, @OccurredAtUtc::timestamptz, @DimensionSetId::uuid, @Amount::numeric);",
                    new
                    {
                        DocumentId = Guid.CreateVersion7(),
                        OccurredAtUtc = new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc),
                        DimensionSetId = dimSetId,
                        Amount = 1m
                    });
            }

            var url = "/api/payables/reconciliation?fromMonthInclusive=2026-02-01&toMonthInclusive=2026-02-01&mode=Movement";
            var report = await client.GetFromJsonAsync<PayablesReconciliationReport>(url);

            report.Should().NotBeNull();
            report!.Mode.Should().Be(PayablesReconciliationMode.Movement);
            report.RowCount.Should().Be(1);
            report.MismatchRowCount.Should().Be(1);
            report.TotalDiff.Should().Be(-1m);

            report.Rows.Should().ContainSingle(r =>
                r.VendorId == vendor.Id &&
                r.PropertyId == property.Id &&
                r.ApNet == 40m &&
                r.OpenItemsNet == 41m &&
                r.Diff == -1m &&
                r.RowKind == PayablesReconciliationRowKind.Mismatch &&
                r.HasDiff);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task Reconciliation_configuration_validation_covers_missing_invalid_null_duplicate_and_unknown_dependencies()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var reconciliation = (PostgresPayablesReconciliationService)scope.ServiceProvider
                .GetRequiredService<IPayablesReconciliationService>();
            var request = new PayablesReconciliationRequest(
                new DateOnly(2026, 2, 1),
                new DateOnly(2026, 2, 1));

            Func<Task> missingPolicy = async () => await reconciliation.GetAsync(request);
            await missingPolicy.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage("*accounting policy is missing*");

            var defaults = await setup.EnsureDefaultsAsync(CancellationToken.None);

            Func<Task> invalidMode = async () => await reconciliation.GetAsync(
                request with { Mode = (PayablesReconciliationMode)int.MaxValue });
            await invalidMode.Should().ThrowAsync<NgbArgumentInvalidException>();

            await catalogs.UpdateAsync(
                PropertyManagementCodes.AccountingPolicy,
                defaults.AccountingPolicyCatalogId,
                Payload(new { ap_vendors_account_id = (Guid?)null }),
                CancellationToken.None);
            Func<Task> missingApAccount = async () => await reconciliation.ReadRequiredPolicyAsync(default);
            await missingApAccount.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage("*no ap_vendors_account_id*");

            await catalogs.UpdateAsync(
                PropertyManagementCodes.AccountingPolicy,
                defaults.AccountingPolicyCatalogId,
                Payload(new
                {
                    ap_vendors_account_id = defaults.AccountsPayableVendorsAccountId,
                    payables_open_items_register_id = (Guid?)null
                }),
                CancellationToken.None);
            Func<Task> missingOpenItemsRegister = async () => await reconciliation.ReadRequiredPolicyAsync(default);
            await missingOpenItemsRegister.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage("*no payables_open_items_register_id*");

            await catalogs.UpdateAsync(
                PropertyManagementCodes.AccountingPolicy,
                defaults.AccountingPolicyCatalogId,
                Payload(new
                {
                    ap_vendors_account_id = defaults.AccountsPayableVendorsAccountId,
                    payables_open_items_register_id = defaults.PayablesOpenItemsOperationalRegisterId
                }),
                CancellationToken.None);

            Func<Task> unknownRegister = async () => await reconciliation
                .ReadOperationalRegisterTableCodeOrThrowAsync(Guid.NewGuid(), default);
            await unknownRegister.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage("*does not exist*");

            await catalogs.CreateAsync(
                PropertyManagementCodes.AccountingPolicy,
                Payload(new
                {
                    display = "Duplicate policy for configuration validation",
                    ap_vendors_account_id = defaults.AccountsPayableVendorsAccountId,
                    payables_open_items_register_id = defaults.PayablesOpenItemsOperationalRegisterId
                }),
                CancellationToken.None);
            Func<Task> duplicatePolicy = async () => await reconciliation.ReadRequiredPolicyAsync(default);
            await duplicatePolicy.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage("*Multiple pm.accounting_policy records*");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    private static RecordPayload Payload(object obj)
    {
        var el = JsonSerializer.SerializeToElement(obj);
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
