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
public sealed class PmReceivablesCustomApply_ExecuteEndpoint_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmReceivablesCustomApply_ExecuteEndpoint_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ExecuteCustomApply_CreatesAndPostsApplyDocs_AndUpdatesOpenItems()
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

            var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
            var rentType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));

            var c1 = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                display = "RC-1",
                lease_id = lease.Id,
                charge_type_id = rentType.Id,
                due_on_utc = "2026-02-05",
                amount = "100.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, c1.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var c2 = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                display = "RC-2",
                lease_id = lease.Id,
                charge_type_id = rentType.Id,
                due_on_utc = "2026-02-10",
                amount = "40.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, c2.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                display = "RP-1",
                lease_id = lease.Id,
                received_on_utc = "2026-02-12",
                amount = "120.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var req = new ReceivablesCustomApplyExecuteRequest(
                CreditDocumentId: payment.Id,
                Applies:
                [
                    new ReceivablesCustomApplyLine(c1.Id, 80m),
                    new ReceivablesCustomApplyLine(c2.Id, 40m)
                ]);

            var http = await client.PostAsJsonAsync("/api/receivables/apply/custom/execute", req);
            http.EnsureSuccessStatusCode();

            var resp = await http.Content.ReadFromJsonAsync<ReceivablesCustomApplyExecuteResponse>();
            resp.Should().NotBeNull();
            resp!.CreditDocumentId.Should().Be(payment.Id);
            resp.TotalApplied.Should().Be(120m);
            resp.RemainingCredit.Should().Be(0m);
            resp.ExecutedApplies.Should().HaveCount(2);

            // Docs exist and are posted.
            foreach (var x in resp.ExecutedApplies)
            {
                var d = await documents.GetByIdAsync(PropertyManagementCodes.ReceivableApply, x.ApplyId, CancellationToken.None);
                d.Status.Should().Be(DocumentStatus.Posted);
            }

            await using (var conn = new NpgsqlConnection(_fixture.ConnectionString))
            {
                await conn.OpenAsync();
                (await conn.ExecuteScalarAsync<int>("select count(*) from doc_pm_receivable_apply;")).Should().Be(2);
            }

            var detailsUrl = $"/api/receivables/open-items/details?partyId={party.Id}&propertyId={property.Id}&leaseId={lease.Id}";
            var open = await client.GetFromJsonAsync<ReceivablesOpenItemsDetailsResponse>(detailsUrl);
            open.Should().NotBeNull();

            open!.TotalOutstanding.Should().Be(20m);
            open.TotalCredit.Should().Be(0m);
            open.Credits.Should().BeEmpty();
            open.Charges.Should().HaveCount(1);
            open.Charges[0].ChargeDocumentId.Should().Be(c1.Id);
            open.Charges[0].OutstandingAmount.Should().Be(20m);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task ExecuteCustomApply_WhenAnyLineInvalid_IsAtomic_NoPartialWrites()
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

            var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
            var rentType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));

            var c1 = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                display = "RC-1",
                lease_id = lease.Id,
                charge_type_id = rentType.Id,
                due_on_utc = "2026-02-05",
                amount = "100.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, c1.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var c2 = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                display = "RC-2",
                lease_id = lease.Id,
                charge_type_id = rentType.Id,
                due_on_utc = "2026-02-10",
                amount = "40.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, c2.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                display = "RP-1",
                lease_id = lease.Id,
                received_on_utc = "2026-02-12",
                amount = "120.00",
            }), CancellationToken.None);
            (await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            // Second line over-applies c2 (outstanding=40).
            var req = new ReceivablesCustomApplyExecuteRequest(
                CreditDocumentId: payment.Id,
                Applies:
                [
                    new ReceivablesCustomApplyLine(c1.Id, 80m),
                    new ReceivablesCustomApplyLine(c2.Id, 50m)
                ]);

            var http = await client.PostAsJsonAsync("/api/receivables/apply/custom/execute", req);
            http.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            await using (var conn = new NpgsqlConnection(_fixture.ConnectionString))
            {
                await conn.OpenAsync();
                (await conn.ExecuteScalarAsync<int>("select count(*) from doc_pm_receivable_apply;")).Should().Be(0);
            }

            var detailsUrl = $"/api/receivables/open-items/details?partyId={party.Id}&propertyId={property.Id}&leaseId={lease.Id}";
            var open = await client.GetFromJsonAsync<ReceivablesOpenItemsDetailsResponse>(detailsUrl);
            open.Should().NotBeNull();
            open!.TotalOutstanding.Should().Be(140m);
            open.TotalCredit.Should().Be(120m);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
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
