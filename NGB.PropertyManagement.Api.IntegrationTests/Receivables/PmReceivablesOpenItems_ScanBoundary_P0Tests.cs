using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Runtime;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Receivables;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReceivablesOpenItems_ScanBoundary_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmReceivablesOpenItems_ScanBoundary_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetOpenItems_IncludesFutureDueCharges_WhenTheyAreAlreadyPosted()
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

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var leaseStart = new DateOnly(today.Year, today.Month, 1);
            var futureDue = leaseStart.AddMonths(2).AddDays(4); // 5th day of that month

            var lease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
            {
                display = "Lease: P @ A",

                property_id = property.Id,
                start_on_utc = leaseStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                rent_amount = "1000.00"
            }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);

            var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
            var rentType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));

            var charge = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
            {
                display = "RC-FUTURE",
                lease_id = lease.Id,
                charge_type_id = rentType.Id,
                due_on_utc = futureDue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                amount = "100.00",
            }), CancellationToken.None);

            (await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var url = $"/api/receivables/open-items?leaseId={lease.Id}";
            var resp = await client.GetFromJsonAsync<ReceivablesOpenItemsDetailsResponse>(url);
            resp.Should().NotBeNull();

            resp!.Charges.Should().ContainSingle(x => x.ChargeDocumentId == charge.Id && x.OutstandingAmount == 100m);
            resp.Credits.Should().BeEmpty();

            resp.TotalOutstanding.Should().Be(100m);
            resp.TotalCredit.Should().Be(0m);
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
