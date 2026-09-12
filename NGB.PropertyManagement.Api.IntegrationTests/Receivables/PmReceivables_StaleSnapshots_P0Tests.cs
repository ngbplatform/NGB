using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Api.IntegrationTests.Support;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Runtime;
using NGB.PropertyManagement.Runtime.DocumentActions;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Receivables;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReceivables_StaleSnapshots_P0Tests(PmIntegrationFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Backdated_rent_and_apply_expose_current_open_items_and_action_availability_before_projection_rebuild()
    {
        await using var scenario = await Scenario.CreateAsync(fixture);
        await scenario.RentAsync("2027-01-01", 10m);
        await scenario.FinalizeAsync();

        // The January snapshot predates this new September charge, just as in RC-2026-3329947.
        var chargeId = await scenario.RentAsync("2026-09-01", 555m);
        await scenario.AssertApplyActionAsync(PropertyManagementCodes.RentCharge, chargeId, true);
        await scenario.AssertOutstandingAsync(chargeId, 555m);
        await scenario.FinalizeAsync();
        await scenario.AssertOutstandingAsync(chargeId, 555m);

        var paymentId = await scenario.PaymentAsync("2026-09-01", 555m);
        await scenario.AssertApplyActionAsync(PropertyManagementCodes.ReceivablePayment, paymentId, true);
        await scenario.FinalizeAsync();

        var applyId = await scenario.PostAsync(PropertyManagementCodes.ReceivableApply, new
        {
            credit_document_id = paymentId, charge_document_id = chargeId,
            applied_on_utc = "2026-09-01", amount = 555m
        });
        await scenario.AssertOutstandingAsync(chargeId, 0m);
        await scenario.AssertApplyActionAsync(PropertyManagementCodes.RentCharge, chargeId, false);
        await scenario.AssertApplyActionAsync(PropertyManagementCodes.ReceivablePayment, paymentId, false);
        await scenario.FinalizeAsync();
        await scenario.AssertOutstandingAsync(chargeId, 0m);

        await scenario.Documents.UnpostAsync(PropertyManagementCodes.ReceivableApply, applyId, default);
        await scenario.AssertOutstandingAsync(chargeId, 555m);
        await scenario.AssertApplyActionAsync(PropertyManagementCodes.RentCharge, chargeId, true);
        await scenario.Documents.RepostAsync(PropertyManagementCodes.RentCharge, chargeId, default);
        await scenario.AssertOutstandingAsync(chargeId, 555m);
        await scenario.Documents.UnpostAsync(PropertyManagementCodes.RentCharge, chargeId, default);
        await scenario.AssertOutstandingAsync(chargeId, 0m);
        await scenario.AssertApplyActionAsync(PropertyManagementCodes.RentCharge, chargeId, false);
    }

    private sealed class Scenario(PmApiFactory factory, AsyncServiceScope scope, HttpClient client) : IAsyncDisposable
    {
        public IServiceProvider Services => scope.ServiceProvider;
        public HttpClient Client => client;
        public IDocumentService Documents => Services.GetRequiredService<IDocumentService>();
        public Guid LeaseId { get; private set; }
        public Guid RegisterId { get; private set; }

        public static async Task<Scenario> CreateAsync(PmIntegrationFixture fixture)
        {
            var factory = new PmApiFactory(fixture);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            var scenario = new Scenario(factory, factory.Services.CreateAsyncScope(), client);
            await scenario.Services.GetRequiredService<IPropertyManagementSetupService>().EnsureDefaultsAsync(default);
            var catalogs = scenario.Services.GetRequiredService<ICatalogService>();
            var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "Snapshot tenant" }), default);
            var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
            {
                kind = "Building", display = "Snapshot building", address_line1 = "Test", city = "Hoboken", state = "NJ", zip = "07030"
            }), default);
            var property = await catalogs.CreateAsync(PropertyManagementCodes.Property,
                Payload(new { kind = "Unit", parent_property_id = building.Id, unit_no = "101" }), default);
            var lease = await scenario.Documents.CreateDraftAsync(PropertyManagementCodes.Lease,
                Payload(new { property_id = property.Id, start_on_utc = "2026-01-01", rent_amount = 1000m }, LeaseParts.PrimaryTenant(party.Id)), default);
            scenario.LeaseId = lease.Id;
            var policy = await scenario.Services.GetRequiredService<IPropertyManagementAccountingPolicyReader>().GetRequiredAsync(default);
            scenario.RegisterId = policy.ReceivablesOpenItemsOperationalRegisterId;
            return scenario;
        }

        public async Task<Guid> PostAsync(string type, object fields)
        {
            var doc = await Documents.CreateDraftAsync(type, Payload(fields), default);
            await Documents.PostAsync(type, doc.Id, default);
            return doc.Id;
        }

        public Task<Guid> RentAsync(string due, decimal amount) => PostAsync(PropertyManagementCodes.RentCharge,
            new { lease_id = LeaseId, period_from_utc = due, period_to_utc = due, due_on_utc = due, amount });

        public Task<Guid> PaymentAsync(string date, decimal amount) => PostAsync(PropertyManagementCodes.ReceivablePayment,
            new { lease_id = LeaseId, received_on_utc = date, amount });

        public Task<int> FinalizeAsync() => Services.GetRequiredService<IOperationalRegisterFinalizationRunner>()
            .FinalizeRegisterDirtyAsync(RegisterId, maxPeriods: 20, ct: default);

        public async Task AssertApplyActionAsync(string type, Guid id, bool allowed)
        {
            var action = await Client.GetDocumentActionAsync(type, id, PropertyManagementDocumentActionCodes.OpenReceivablesReconciliation.Value);
            action.IsAllowed.Should().Be(allowed);
        }

        public async Task AssertOutstandingAsync(Guid chargeId, decimal expected)
        {
            var open = await Client.GetFromJsonAsync<ReceivablesOpenItemsDetailsResponse>(
                $"/api/receivables/open-items?leaseId={LeaseId}");
            open.Should().NotBeNull();
            if (expected == 0m)
                open!.Charges.Should().NotContain(x => x.ChargeDocumentId == chargeId);
            else
                open!.Charges.Should().ContainSingle(x => x.ChargeDocumentId == chargeId && x.OutstandingAmount == expected);
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await scope.DisposeAsync();
            await factory.DisposeAsync();
        }
    }

    private static RecordPayload Payload(object fields, IReadOnlyDictionary<string, RecordPartPayload>? parts = null)
        => new(JsonSerializer.SerializeToElement(fields).EnumerateObject().ToDictionary(x => x.Name, x => x.Value), parts);
}
