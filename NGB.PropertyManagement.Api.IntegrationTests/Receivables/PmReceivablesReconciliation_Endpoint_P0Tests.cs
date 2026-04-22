using System.Net;
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
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Runtime;
using Npgsql;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Receivables;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReceivablesReconciliation_Endpoint_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmReceivablesReconciliation_Endpoint_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

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

            var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "Tenant One" }), CancellationToken.None);
            var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
            {
                kind = "Building",
                display = "101 Main St, Hoboken, NJ 07030",
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
                display = "Lease draft override",

                property_id = property.Id,
                start_on_utc = "2026-02-01",
                rent_amount = "1000.00"
            }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);

            var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
            var rentType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));

            var charge = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                display = "RC-1",
                lease_id = lease.Id,
                charge_type_id = rentType.Id,
                due_on_utc = "2026-02-05",
                amount = "100.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                display = "RP-1",
                lease_id = lease.Id,
                received_on_utc = "2026-02-07",
                amount = "60.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var url = "/api/receivables/reconciliation?fromMonthInclusive=2026-02-01&toMonthInclusive=2026-02-01";
            var report = await client.GetFromJsonAsync<ReceivablesReconciliationReport>(url);

            report.Should().NotBeNull();
            report!.Mode.Should().Be(ReceivablesReconciliationMode.Movement);
            report.RowCount.Should().Be(1);
            report.MismatchRowCount.Should().Be(0);
            report.TotalDiff.Should().Be(0m);

            report.Rows.Should().ContainSingle(r =>
                r.PartyId == party.Id &&
                r.PartyDisplay == party.Display &&
                r.PropertyId == property.Id &&
                r.PropertyDisplay == property.Display &&
                r.LeaseId == lease.Id &&
                r.LeaseDisplay == lease.Display &&
                r.ArNet == 40m &&
                r.OpenItemsNet == 40m &&
                r.Diff == 0m &&
                r.RowKind == ReceivablesReconciliationRowKind.Matched &&
                r.HasDiff == false);
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

            var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "Tenant One" }), CancellationToken.None);
            var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
            {
                kind = "Building",
                display = "101 Main St, Hoboken, NJ 07030",
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
                display = "Lease draft override",

                property_id = property.Id,
                start_on_utc = "2026-02-01",
                rent_amount = "1000.00"
            }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);

            var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
            var rentType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));

            var charge = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                display = "RC-1",
                lease_id = lease.Id,
                charge_type_id = rentType.Id,
                due_on_utc = "2026-02-05",
                amount = "100.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                display = "RP-1",
                lease_id = lease.Id,
                received_on_utc = "2026-02-07",
                amount = "60.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            // Inject a deliberate drift into open-items movements (+1) using raw SQL.
            await using (var conn = new NpgsqlConnection(_fixture.ConnectionString))
            {
                await conn.OpenAsync();

                var tableCode = await conn.QuerySingleAsync<string>(
                    "SELECT table_code FROM operational_registers WHERE register_id = @Id::uuid;",
                    new { Id = setupResult.ReceivablesOpenItemsOperationalRegisterId });

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

            var url = "/api/receivables/reconciliation?fromMonthInclusive=2026-02-01&toMonthInclusive=2026-02-01&mode=Movement";
            var report = await client.GetFromJsonAsync<ReceivablesReconciliationReport>(url);

            report.Should().NotBeNull();
            report!.Mode.Should().Be(ReceivablesReconciliationMode.Movement);
            report.RowCount.Should().Be(1);
            report.MismatchRowCount.Should().Be(1);
            report.TotalDiff.Should().Be(-1m);

            report.Rows.Should().ContainSingle(r =>
                r.PartyId == party.Id &&
                r.PartyDisplay == party.Display &&
                r.PropertyId == property.Id &&
                r.PropertyDisplay == property.Display &&
                r.LeaseId == lease.Id &&
                r.LeaseDisplay == lease.Display &&
                r.ArNet == 40m &&
                r.OpenItemsNet == 41m &&
                r.Diff == -1m &&
                r.RowKind == ReceivablesReconciliationRowKind.Mismatch &&
                r.HasDiff);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task GetReconciliation_WhenPriorMonthChargeAndCurrentMonthPayment_BalanceModeReturnsCutoffBalanceAtToMonth()
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

            var (party, property, lease, rentType) = await CreateLeaseFixtureAsync(catalogs, documents, CancellationToken.None);

            var charge = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                display = "RC-1",
                lease_id = lease.Id,
                charge_type_id = rentType.Id,
                due_on_utc = "2026-02-05",
                amount = "100.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                display = "RP-1",
                lease_id = lease.Id,
                received_on_utc = "2026-03-07",
                amount = "60.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var movement = await client.GetFromJsonAsync<ReceivablesReconciliationReport>(
                "/api/receivables/reconciliation?fromMonthInclusive=2026-03-01&toMonthInclusive=2026-03-01&mode=Movement");
            var balance = await client.GetFromJsonAsync<ReceivablesReconciliationReport>(
                "/api/receivables/reconciliation?fromMonthInclusive=2026-03-01&toMonthInclusive=2026-03-01&mode=Balance");

            movement.Should().NotBeNull();
            movement!.Mode.Should().Be(ReceivablesReconciliationMode.Movement);
            movement.TotalArNet.Should().Be(-60m);
            movement.TotalOpenItemsNet.Should().Be(-60m);
            movement.TotalDiff.Should().Be(0m);
            movement.Rows.Should().ContainSingle(r =>
                r.PartyId == party.Id &&
                r.PartyDisplay == party.Display &&
                r.PropertyId == property.Id &&
                r.PropertyDisplay == property.Display &&
                r.LeaseId == lease.Id &&
                r.LeaseDisplay == lease.Display &&
                r.ArNet == -60m &&
                r.OpenItemsNet == -60m &&
                r.Diff == 0m &&
                r.RowKind == ReceivablesReconciliationRowKind.Matched &&
                r.HasDiff == false);

            balance.Should().NotBeNull();
            balance!.Mode.Should().Be(ReceivablesReconciliationMode.Balance);
            balance.TotalArNet.Should().Be(40m);
            balance.TotalOpenItemsNet.Should().Be(40m);
            balance.TotalDiff.Should().Be(0m);
            balance.Rows.Should().ContainSingle(r =>
                r.PartyId == party.Id &&
                r.PartyDisplay == party.Display &&
                r.PropertyId == property.Id &&
                r.PropertyDisplay == property.Display &&
                r.LeaseId == lease.Id &&
                r.LeaseDisplay == lease.Display &&
                r.ArNet == 40m &&
                r.OpenItemsNet == 40m &&
                r.Diff == 0m &&
                r.RowKind == ReceivablesReconciliationRowKind.Matched &&
                r.HasDiff == false);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task GetReconciliation_WhenPriorMonthOpenItemsDriftExists_BalanceModeKeepsTheHistoricalDriftVisible()
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

            var (party, property, lease, rentType) = await CreateLeaseFixtureAsync(catalogs, documents, CancellationToken.None);

            var charge = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                display = "RC-1",
                lease_id = lease.Id,
                charge_type_id = rentType.Id,
                due_on_utc = "2026-02-05",
                amount = "100.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                display = "RP-1",
                lease_id = lease.Id,
                received_on_utc = "2026-03-07",
                amount = "60.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            await InjectOpenItemsDriftAsync(setupResult.ReceivablesOpenItemsOperationalRegisterId, charge.Id, new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc), 1m);

            var movement = await client.GetFromJsonAsync<ReceivablesReconciliationReport>(
                "/api/receivables/reconciliation?fromMonthInclusive=2026-03-01&toMonthInclusive=2026-03-01&mode=Movement");
            var balance = await client.GetFromJsonAsync<ReceivablesReconciliationReport>(
                "/api/receivables/reconciliation?fromMonthInclusive=2026-03-01&toMonthInclusive=2026-03-01&mode=Balance");

            movement.Should().NotBeNull();
            movement!.Mode.Should().Be(ReceivablesReconciliationMode.Movement);
            movement.TotalDiff.Should().Be(0m);
            movement.Rows.Should().ContainSingle(r =>
                r.PartyId == party.Id &&
                r.PartyDisplay == party.Display &&
                r.PropertyId == property.Id &&
                r.PropertyDisplay == property.Display &&
                r.LeaseId == lease.Id &&
                r.LeaseDisplay == lease.Display &&
                r.ArNet == -60m &&
                r.OpenItemsNet == -60m &&
                r.Diff == 0m &&
                r.RowKind == ReceivablesReconciliationRowKind.Matched &&
                r.HasDiff == false);

            balance.Should().NotBeNull();
            balance!.Mode.Should().Be(ReceivablesReconciliationMode.Balance);
            balance.RowCount.Should().Be(1);
            balance.MismatchRowCount.Should().Be(1);
            balance.TotalArNet.Should().Be(40m);
            balance.TotalOpenItemsNet.Should().Be(41m);
            balance.TotalDiff.Should().Be(-1m);
            balance.Rows.Should().ContainSingle(r =>
                r.PartyId == party.Id &&
                r.PartyDisplay == party.Display &&
                r.PropertyId == property.Id &&
                r.PropertyDisplay == property.Display &&
                r.LeaseId == lease.Id &&
                r.LeaseDisplay == lease.Display &&
                r.ArNet == 40m &&
                r.OpenItemsNet == 41m &&
                r.Diff == -1m &&
                r.RowKind == ReceivablesReconciliationRowKind.Mismatch &&
                r.HasDiff);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }


    [Fact]
    public async Task GetReconciliation_WhenFromMonthIsNotMonthStart_Returns400_WithFieldError()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

            using var resp = await client.GetAsync(
                "/api/receivables/reconciliation?fromMonthInclusive=2026-03-15&toMonthInclusive=2026-03-01&mode=Balance");

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            root.GetProperty("error").GetProperty("code").GetString().Should().Be("ngb.validation.argument_out_of_range");
            root.GetProperty("error").GetProperty("errors").GetProperty("FromMonthInclusive").EnumerateArray().Select(x => x.GetString())
                .Should().Contain("From month must be the first day of a month.");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task GetReconciliation_WhenToMonthIsEarlierThanFromMonth_Returns400_WithFieldError()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

            using var resp = await client.GetAsync(
                "/api/receivables/reconciliation?fromMonthInclusive=2026-03-01&toMonthInclusive=2026-02-01&mode=Balance");

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            root.GetProperty("error").GetProperty("code").GetString().Should().Be("ngb.validation.argument_out_of_range");
            root.GetProperty("error").GetProperty("errors").GetProperty("ToMonthInclusive").EnumerateArray().Select(x => x.GetString())
                .Should().Contain("To month must be on or after From month.");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task GetReconciliation_WhenAccountingPolicyIsMissing_Returns500_WithConfigurationViolation()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

            using var resp = await client.GetAsync(
                "/api/receivables/reconciliation?fromMonthInclusive=2026-03-01&toMonthInclusive=2026-03-01&mode=Balance");

            resp.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            root.GetProperty("error").GetProperty("code").GetString().Should().Be("ngb.configuration.violation");
            root.GetProperty("error").GetProperty("kind").GetString().Should().Be("Configuration");
            var context = root.GetProperty("error").GetProperty("context");
            context.GetProperty("catalogCode").GetString().Should().Be(PropertyManagementCodes.AccountingPolicy);
            context.GetProperty("headTable").GetString().Should().Be("cat_pm_accounting_policy");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task GetReconciliation_WhenPolicyArAccountIsMissing_Returns500_WithConfigurationViolation()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            await setup.EnsureDefaultsAsync(CancellationToken.None);

            await using (var conn = new NpgsqlConnection(_fixture.ConnectionString))
            {
                await conn.OpenAsync();
                await conn.ExecuteAsync("UPDATE cat_pm_accounting_policy SET ar_tenants_account_id = NULL;");
            }

            using var resp = await client.GetAsync(
                "/api/receivables/reconciliation?fromMonthInclusive=2026-03-01&toMonthInclusive=2026-03-01&mode=Balance");

            resp.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            root.GetProperty("error").GetProperty("code").GetString().Should().Be("ngb.configuration.violation");
            root.GetProperty("error").GetProperty("kind").GetString().Should().Be("Configuration");
            var context = root.GetProperty("error").GetProperty("context");
            context.GetProperty("catalogCode").GetString().Should().Be(PropertyManagementCodes.AccountingPolicy);
            context.GetProperty("field").GetString().Should().Be("ar_tenants_account_id");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task GetReconciliation_WhenOpenItemsMovementsTableIsMissing_ReturnsGlOnlyRow_InBalanceMode()
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
            var (party, property, lease, rentType) = await CreateLeaseFixtureAsync(catalogs, documents, CancellationToken.None);

            var charge = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                display = "RC-1",
                lease_id = lease.Id,
                charge_type_id = rentType.Id,
                due_on_utc = "2026-02-05",
                amount = "100.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                display = "RP-1",
                lease_id = lease.Id,
                received_on_utc = "2026-02-07",
                amount = "60.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            await using (var conn = new NpgsqlConnection(_fixture.ConnectionString))
            {
                await conn.OpenAsync();

                var tableCode = await conn.QuerySingleAsync<string>(
                    "SELECT table_code FROM operational_registers WHERE register_id = @Id::uuid;",
                    new { Id = setupResult.ReceivablesOpenItemsOperationalRegisterId });

                await conn.ExecuteAsync($"DROP TABLE IF EXISTS opreg_{tableCode}__movements CASCADE;");
            }

            var report = await client.GetFromJsonAsync<ReceivablesReconciliationReport>(
                "/api/receivables/reconciliation?fromMonthInclusive=2026-02-01&toMonthInclusive=2026-02-01&mode=Balance");

            report.Should().NotBeNull();
            report!.Mode.Should().Be(ReceivablesReconciliationMode.Balance);
            report.RowCount.Should().Be(1);
            report.MismatchRowCount.Should().Be(1);
            report.TotalArNet.Should().Be(40m);
            report.TotalOpenItemsNet.Should().Be(0m);
            report.TotalDiff.Should().Be(40m);
            report.Rows.Should().ContainSingle(r =>
                r.PartyId == party.Id &&
                r.PartyDisplay == party.Display &&
                r.PropertyId == property.Id &&
                r.PropertyDisplay == property.Display &&
                r.LeaseId == lease.Id &&
                r.LeaseDisplay == lease.Display &&
                r.ArNet == 40m &&
                r.OpenItemsNet == 0m &&
                r.Diff == 40m &&
                r.RowKind == ReceivablesReconciliationRowKind.GlOnly &&
                r.HasDiff);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    private static async Task<(NGB.Contracts.Services.CatalogItemDto Party, NGB.Contracts.Services.CatalogItemDto Property, NGB.Contracts.Services.DocumentDto Lease, NGB.Contracts.Services.CatalogItemDto RentType)> CreateLeaseFixtureAsync(
        ICatalogService catalogs,
        IDocumentService documents,
        CancellationToken ct)
    {
        var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "Tenant One" }), ct);
        var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            display = "101 Main St, Hoboken, NJ 07030",
            address_line1 = "A",
            city = "Hoboken",
            state = "NJ",
            zip = "07030"
        }), ct);

        var property = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Unit",
            parent_property_id = building.Id,
            unit_no = "101"
        }), ct);

        var lease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
        {
            display = "Lease draft override",
            property_id = property.Id,
            start_on_utc = "2026-02-01",
            rent_amount = "1000.00"
        }, LeaseParts.PrimaryTenant(party.Id)), ct);

        var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), ct);
        var rentType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));

        return (party, property, lease, rentType);
    }

    private async Task InjectOpenItemsDriftAsync(Guid registerId, Guid documentId, DateTime occurredAtUtc, decimal amount)
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        var tableCode = await conn.QuerySingleAsync<string>(
            "SELECT table_code FROM operational_registers WHERE register_id = @Id::uuid;",
            new { Id = registerId });

        var table = $"opreg_{tableCode}__movements";

        var dimSetId = await conn.QuerySingleAsync<Guid>(
            $"SELECT dimension_set_id FROM {table} WHERE document_id = @DocId::uuid LIMIT 1;",
            new { DocId = documentId });

        await conn.ExecuteAsync(
            $"INSERT INTO {table} (document_id, occurred_at_utc, dimension_set_id, amount) VALUES (@DocumentId::uuid, @OccurredAtUtc::timestamptz, @DimensionSetId::uuid, @Amount::numeric);",
            new
            {
                DocumentId = Guid.CreateVersion7(),
                OccurredAtUtc = occurredAtUtc,
                DimensionSetId = dimSetId,
                Amount = amount
            });
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
