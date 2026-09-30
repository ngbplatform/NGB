using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Api.IntegrationTests.Support;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Runtime;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Receivables;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReceivableCreditMemo_ApplyWorkflows_P0Tests(PmIntegrationFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("batch", false, 0)]
    [InlineData("batch", true, 0)]
    [InlineData("batch", false, 20)]
    [InlineData("fifo", false, 0)]
    [InlineData("custom", false, 0)]
    public async Task Credit_memo_can_be_applied_and_unapplied_with_balances_persisted(
        string workflow, bool createDrafts, int paymentAmount)
    {
        const decimal chargeAmount = 582.80m;
        const decimal creditAmount = 29.839m;
        await using var factory = new PmApiFactory(fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        await using var scope = factory.Services.CreateAsyncScope();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var context = await CreateContextAsync(scope.ServiceProvider, chargeAmount, creditAmount, paymentAmount);
        var detailsUrl = $"/api/receivables/open-items/details?partyId={context.PartyId}&propertyId={context.PropertyId}&leaseId={context.LeaseId}";

        var before = await client.GetFromJsonAsync<ReceivablesOpenItemsDetailsResponse>(detailsUrl);
        before!.TotalOutstanding.Should().Be(chargeAmount);
        before.TotalCredit.Should().Be(creditAmount + paymentAmount);

        HttpResponseMessage executionHttp;
        if (workflow == "batch")
        {
            using var suggestHttp = await client.PostAsJsonAsync("/api/receivables/apply/fifo/suggest/lease",
                new ReceivablesSuggestFifoApplyRequest(context.LeaseId, CreateDrafts: createDrafts));
            suggestHttp.StatusCode.Should().Be(HttpStatusCode.OK, await suggestHttp.Content.ReadAsStringAsync());
            var suggestion = (await suggestHttp.Content.ReadFromJsonAsync<ReceivablesSuggestFifoApplyResponse>())!;
            suggestion.SuggestedApplies.Should().ContainSingle(x =>
                x.CreditDocumentId == context.CreditMemoId
                && x.CreditDocumentType == PropertyManagementCodes.ReceivableCreditMemo
                && x.Amount == creditAmount);
            suggestion.SuggestedApplies.Should().OnlyContain(x => x.ApplyId.HasValue == createDrafts);

            executionHttp = await client.PostAsJsonAsync("/api/receivables/apply/batch",
                new ReceivablesApplyBatchRequest(suggestion.SuggestedApplies
                    .Select(x => new ReceivablesApplyBatchItem(x.ApplyId, x.ApplyPayload)).ToArray()));
        }
        else if (workflow == "fifo")
        {
            executionHttp = await client.PostAsJsonAsync("/api/receivables/apply/fifo/execute",
                new ReceivablesFifoApplyExecuteRequest(context.CreditMemoId, MaxApplications: null));
        }
        else
        {
            executionHttp = await client.PostAsJsonAsync("/api/receivables/apply/custom/execute",
                new ReceivablesCustomApplyExecuteRequest(context.CreditMemoId,
                    [new ReceivablesCustomApplyLine(context.ChargeId, creditAmount)]));
        }

        using (executionHttp)
        {
            var body = await executionHttp.Content.ReadAsStringAsync();
            executionHttp.StatusCode.Should().Be(HttpStatusCode.OK, body);
            using var result = JsonDocument.Parse(body);
            result.RootElement.GetProperty("totalApplied").GetDecimal().Should().Be(creditAmount + paymentAmount);
            var applies = result.RootElement.GetProperty("executedApplies").EnumerateArray().ToArray();
            applies.Should().HaveCount(paymentAmount > 0 ? 2 : 1);
            foreach (var apply in applies)
            {
                var document = await documents.GetByIdAsync(PropertyManagementCodes.ReceivableApply,
                    apply.GetProperty("applyId").GetGuid(), CancellationToken.None);
                document.Status.Should().Be(DocumentStatus.Posted);
            }
        }

        var after = (await client.GetFromJsonAsync<ReceivablesOpenItemsDetailsResponse>(detailsUrl))!;
        after.TotalOutstanding.Should().Be(chargeAmount - creditAmount - paymentAmount);
        after.TotalCredit.Should().Be(0m);
        after.Credits.Should().BeEmpty();
        var memoApply = after.Allocations.Should().ContainSingle(x => x.CreditDocumentId == context.CreditMemoId).Which;
        memoApply.CreditDocumentType.Should().Be(PropertyManagementCodes.ReceivableCreditMemo);
        memoApply.Amount.Should().Be(creditAmount);

        using var unapplyHttp = await client.PostAsync($"/api/receivables/apply/{memoApply.ApplyId}/unapply", null);
        unapplyHttp.StatusCode.Should().Be(HttpStatusCode.OK, await unapplyHttp.Content.ReadAsStringAsync());
        var unapplied = (await unapplyHttp.Content.ReadFromJsonAsync<ReceivablesUnapplyResponse>())!;
        unapplied.CreditDocumentId.Should().Be(context.CreditMemoId);
        unapplied.UnappliedAmount.Should().Be(creditAmount);
        (await documents.GetByIdAsync(PropertyManagementCodes.ReceivableApply, memoApply.ApplyId, CancellationToken.None))
            .Status.Should().Be(DocumentStatus.Draft);

        var restored = (await client.GetFromJsonAsync<ReceivablesOpenItemsDetailsResponse>(detailsUrl))!;
        restored.TotalOutstanding.Should().Be(chargeAmount - paymentAmount);
        restored.TotalCredit.Should().Be(creditAmount);
        restored.Credits.Should().ContainSingle(x =>
            x.CreditDocumentId == context.CreditMemoId && x.AvailableCredit == creditAmount);
        restored.Allocations.Should().NotContain(x => x.ApplyId == memoApply.ApplyId && x.IsPosted);
        if (paymentAmount > 0)
            restored.Allocations.Should().ContainSingle(x =>
                x.CreditDocumentType == PropertyManagementCodes.ReceivablePayment && x.IsPosted && x.Amount == paymentAmount);
    }

    private static async Task<TestContext> CreateContextAsync(
        IServiceProvider services, decimal chargeAmount, decimal creditAmount, decimal paymentAmount)
    {
        await services.GetRequiredService<IPropertyManagementSetupService>().EnsureDefaultsAsync(CancellationToken.None);
        var catalogs = services.GetRequiredService<ICatalogService>();
        var documents = services.GetRequiredService<IDocumentService>();
        var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "Credit memo tenant" }), CancellationToken.None);
        var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building", display = "Credit memo building", address_line1 = "100 Hudson Ave",
            city = "Hoboken", state = "NJ", zip = "07030"
        }), CancellationToken.None);
        var property = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Unit", parent_property_id = building.Id, unit_no = "107"
        }), CancellationToken.None);
        var lease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
        {
            property_id = property.Id, start_on_utc = "2026-02-01", rent_amount = 1000m
        }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);
        var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType,
            new PageRequestDto(0, 50, null), CancellationToken.None);
        var chargeTypeId = chargeTypes.Items.Single(x => x.Display == "Utility").Id;
        var charge = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
        {
            lease_id = lease.Id, charge_type_id = chargeTypeId, due_on_utc = "2026-02-05", amount = chargeAmount
        }), CancellationToken.None);
        await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge.Id, CancellationToken.None);
        var creditMemo = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCreditMemo, Payload(new
        {
            lease_id = lease.Id, charge_type_id = chargeTypeId, credited_on_utc = "2026-02-07", amount = creditAmount
        }), CancellationToken.None);
        await documents.PostAsync(PropertyManagementCodes.ReceivableCreditMemo, creditMemo.Id, CancellationToken.None);
        if (paymentAmount > 0m)
        {
            var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                lease_id = lease.Id, received_on_utc = "2026-02-06", amount = paymentAmount
            }), CancellationToken.None);
            await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None);
        }

        return new TestContext(party.Id, property.Id, lease.Id, charge.Id, creditMemo.Id);
    }

    private static RecordPayload Payload(object value, IReadOnlyDictionary<string, RecordPartPayload>? parts = null)
        => new(JsonSerializer.SerializeToElement(value).EnumerateObject().ToDictionary(x => x.Name, x => x.Value), parts);

    private sealed record TestContext(Guid PartyId, Guid PropertyId, Guid LeaseId, Guid ChargeId, Guid CreditMemoId);
}
