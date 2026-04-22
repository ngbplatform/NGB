using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Effects;
using NGB.Contracts.Metadata;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Runtime;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Receivables;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReceivablePayment_Effects_Endpoint_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmReceivablePayment_Effects_Endpoint_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetEffects_WhenPaymentIsDraft_DisablesApply_WithRequiresPosted()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var data = await SeedLeaseDataAsync(scope.ServiceProvider, CancellationToken.None);

            var payment = await data.Documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                display = "RP-DRAFT",
                lease_id = data.LeaseId,
                received_on_utc = "2026-02-06",
                amount = "100.00",
            }), CancellationToken.None);

            var effects = await GetEffectsAsync(client, PropertyManagementCodes.ReceivablePayment, payment.Id);

            effects.AccountingEntries.Should().BeEmpty();
            effects.OperationalRegisterMovements.Should().BeEmpty();
            effects.ReferenceRegisterWrites.Should().BeEmpty();
            effects.Ui.Should().NotBeNull();
            effects.Ui!.CanApply.Should().BeFalse();
            effects.Ui.DisabledReasons.Should().ContainKey("apply");
            effects.Ui.DisabledReasons["apply"].Single().ErrorCode.Should().Be("pm.ui.apply.requires_posted");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task GetEffects_WhenPaymentIsPostedAndHasUnappliedCredit_AllowsApply()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var data = await SeedLeaseDataAsync(scope.ServiceProvider, CancellationToken.None);

            var payment = await data.Documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                display = "RP-POSTED",
                lease_id = data.LeaseId,
                received_on_utc = "2026-02-06",
                amount = "100.00",
            }), CancellationToken.None);
            (await data.Documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var effects = await GetEffectsAsync(client, PropertyManagementCodes.ReceivablePayment, payment.Id);

            effects.Ui.Should().NotBeNull();
            effects.Ui!.CanApply.Should().BeTrue();
            effects.Ui.DisabledReasons.Should().NotContainKey("apply");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task GetEffects_AfterPostUnpostPost_ReturnsOnlyCurrentAccountingAndOperationalRegisterEffects()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var data = await SeedLeaseDataAsync(scope.ServiceProvider, CancellationToken.None);

            var payment = await data.Documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                display = "RP-HISTORY-CURRENT",
                lease_id = data.LeaseId,
                received_on_utc = "2026-02-06",
                amount = "7000.00",
            }), CancellationToken.None);

            (await data.Documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);
            (await data.Documents.UnpostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Draft);
            (await data.Documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var effects = await GetEffectsAsync(client, PropertyManagementCodes.ReceivablePayment, payment.Id);

            effects.AccountingEntries.Should().ContainSingle();
            effects.AccountingEntries.Single().Amount.Should().Be(7000m);
            effects.AccountingEntries.Single().IsStorno.Should().BeFalse();

            effects.OperationalRegisterMovements.Should().ContainSingle();
            effects.OperationalRegisterMovements.Single().Resources.Should().ContainSingle(x => x.Code == "amount" && x.Value == -7000m);
            effects.OperationalRegisterMovements.Single().IsStorno.Should().BeFalse();

            effects.ReferenceRegisterWrites.Should().BeEmpty();
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task GetEffects_WhenPaymentIsPosted_ReturnsRealAccountingAndOperationalRegisterEffects()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var data = await SeedLeaseDataAsync(scope.ServiceProvider, CancellationToken.None);

            var payment = await data.Documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
            {
                display = "RP-EFFECTS",
                lease_id = data.LeaseId,
                received_on_utc = "2026-02-06",
                amount = "100.00",
            }), CancellationToken.None);
            (await data.Documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var effects = await GetEffectsAsync(client, PropertyManagementCodes.ReceivablePayment, payment.Id);

            effects.AccountingEntries.Should().ContainSingle();
            var entry = effects.AccountingEntries.Single();
            entry.DocumentId.Should().Be(payment.Id);
            entry.Amount.Should().Be(100m);
            entry.IsStorno.Should().BeFalse();
            entry.DebitAccount.Code.Should().NotBeNullOrWhiteSpace();
            entry.CreditAccount.Code.Should().NotBeNullOrWhiteSpace();

            effects.OperationalRegisterMovements.Should().ContainSingle();
            var movement = effects.OperationalRegisterMovements.Single();
            movement.DocumentId.Should().Be(payment.Id);
            movement.RegisterCode.Should().Be(PropertyManagementCodes.ReceivablesOpenItemsRegisterCode);
            movement.RegisterName.Should().Be("Receivables - Open Items");
            movement.Resources.Should().ContainSingle(x => x.Code == "amount" && x.Value == -100m);
            movement.Dimensions.Should().HaveCount(4);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    private static async Task<LeaseData> SeedLeaseDataAsync(IServiceProvider services, CancellationToken ct)
    {
        var setup = services.GetRequiredService<IPropertyManagementSetupService>();
        var catalogs = services.GetRequiredService<ICatalogService>();
        var documents = services.GetRequiredService<IDocumentService>();

        await setup.EnsureDefaultsAsync(ct);

        var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "P" }), ct);
        var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            display = "A",
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
            display = "Lease: P @ A",
            property_id = property.Id,
            start_on_utc = "2026-02-01",
            rent_amount = "1000.00"
        }, LeaseParts.PrimaryTenant(party.Id)), ct);

        var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), ct);
        var rentType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));

        return new LeaseData(catalogs, documents, party.Id, property.Id, lease.Id, rentType.Id);
    }

    private static async Task<DocumentEffectsDto> GetEffectsAsync(HttpClient client, string documentType, Guid id)
    {
        var effects = await client.GetFromJsonAsync<DocumentEffectsDto>($"/api/documents/{documentType}/{id}/effects");
        effects.Should().NotBeNull();
        return effects!;
    }

    private readonly record struct LeaseData(
        ICatalogService Catalogs,
        IDocumentService Documents,
        Guid PartyId,
        Guid PropertyId,
        Guid LeaseId,
        Guid RentTypeId);

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
