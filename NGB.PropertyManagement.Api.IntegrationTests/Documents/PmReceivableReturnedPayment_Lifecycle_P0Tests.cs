using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Runtime;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReceivableReturnedPayment_Lifecycle_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmReceivableReturnedPayment_Lifecycle_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UnpostAsync_WritesSingleStorno_ForAccountingAndOpenItemsRegister()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
            var opregRead = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var seeded = await SeedPostedReturnedPaymentAsync(scope.ServiceProvider, returnedOnUtc: "2026-02-08", amount: "25.00");

            var unposted = await documents.UnpostAsync(PropertyManagementCodes.ReceivableReturnedPayment, seeded.ReturnedPayment.Id, CancellationToken.None);
            unposted.Status.Should().Be(DocumentStatus.Draft);

            var accounting = await ReadAccountingEntriesAsync(uow, seeded.ReturnedPayment.Id);
            accounting.Should().HaveCount(2);
            accounting.Count(x => x.IsStorno).Should().Be(1);
            accounting.Sum(x => x.IsStorno ? -x.Amount : x.Amount).Should().Be(0m);

            var movements = await ReadMovementsAsync(opregRead, seeded.RegisterId, seeded.ReturnedPayment.Id);
            movements.Should().HaveCount(2);
            movements.Count(x => x.IsStorno).Should().Be(1);
            movements.Sum(x => x.IsStorno ? -Convert.ToDecimal(x.Values["amount"]) : Convert.ToDecimal(x.Values["amount"])).Should().Be(0m);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task RepostAsync_AppendsStornoAndFreshRows_ForAccountingAndOpenItemsRegister()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
            var opregRead = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var seeded = await SeedPostedReturnedPaymentAsync(scope.ServiceProvider, returnedOnUtc: "2026-02-08", amount: "25.00");

            var reposted = await documents.RepostAsync(PropertyManagementCodes.ReceivableReturnedPayment, seeded.ReturnedPayment.Id, CancellationToken.None);
            reposted.Status.Should().Be(DocumentStatus.Posted);

            var accounting = await ReadAccountingEntriesAsync(uow, seeded.ReturnedPayment.Id);
            accounting.Should().HaveCount(3);
            accounting.Count(x => x.IsStorno).Should().Be(1);
            accounting.Count(x => !x.IsStorno).Should().Be(2);
            accounting.Sum(x => x.IsStorno ? -x.Amount : x.Amount).Should().Be(25.00m);

            var movements = await ReadMovementsAsync(opregRead, seeded.RegisterId, seeded.ReturnedPayment.Id);
            movements.Should().HaveCount(3);
            movements.Count(x => x.IsStorno).Should().Be(1);
            movements.Count(x => !x.IsStorno).Should().Be(2);
            movements.Sum(x => x.IsStorno ? -Convert.ToDecimal(x.Values["amount"]) : Convert.ToDecimal(x.Values["amount"])).Should().Be(25.00m);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task PostAsync_AfterUnpost_PostsAgain_AndSecondUnpost_IsAlsoAllowed()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
            var opregRead = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var seeded = await SeedDraftReturnedPaymentAsync(scope.ServiceProvider);

            (await documents.PostAsync(PropertyManagementCodes.ReceivableReturnedPayment, seeded.ReturnedPayment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);
            (await documents.UnpostAsync(PropertyManagementCodes.ReceivableReturnedPayment, seeded.ReturnedPayment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Draft);
            (await documents.PostAsync(PropertyManagementCodes.ReceivableReturnedPayment, seeded.ReturnedPayment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);
            (await documents.UnpostAsync(PropertyManagementCodes.ReceivableReturnedPayment, seeded.ReturnedPayment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Draft);

            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            const string sql = """
SELECT amount AS Amount, is_storno AS IsStorno
FROM accounting_register_main
WHERE document_id = @document_id
ORDER BY entry_id;
""";

            var rows = (await uow.Connection.QueryAsync<AccountingEntryRow>(
                new CommandDefinition(sql, new { document_id = seeded.ReturnedPayment.Id }, uow.Transaction, cancellationToken: CancellationToken.None)))
                .AsList();

            rows.Should().HaveCount(4);
            rows.Count(x => !x.IsStorno).Should().Be(2);
            rows.Count(x => x.IsStorno).Should().Be(2);

            var movements = await opregRead.GetMovementsPageAsync(
                new OperationalRegisterMovementsPageRequest(
                    RegisterId: seeded.RegisterId,
                    FromInclusive: new DateOnly(2026, 2, 1),
                    ToInclusive: new DateOnly(2026, 3, 1),
                    DocumentId: seeded.ReturnedPayment.Id,
                    PageSize: 50),
                CancellationToken.None);

            movements.Lines.Should().HaveCount(6);
            movements.Lines.Sum(x => x.IsStorno ? -Convert.ToDecimal(x.Values["amount"]) : Convert.ToDecimal(x.Values["amount"])).Should().Be(0m);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    private static async Task<(DocumentDto ReturnedPayment, Guid RegisterId)> SeedPostedReturnedPaymentAsync(IServiceProvider services, string returnedOnUtc, string amount)
    {
        var seeded = await SeedDraftReturnedPaymentAsync(services, returnedOnUtc, amount);
        var documents = services.GetRequiredService<IDocumentService>();
        var posted = await documents.PostAsync(PropertyManagementCodes.ReceivableReturnedPayment, seeded.ReturnedPayment.Id, CancellationToken.None);
        posted.Status.Should().Be(DocumentStatus.Posted);
        return (posted, seeded.RegisterId);
    }

    private static async Task<(DocumentDto ReturnedPayment, Guid RegisterId)> SeedDraftReturnedPaymentAsync(IServiceProvider services, string returnedOnUtc = "2026-02-08", string amount = "25.00")
    {
        var setup = services.GetRequiredService<IPropertyManagementSetupService>();
        var catalogs = services.GetRequiredService<ICatalogService>();
        var documents = services.GetRequiredService<IDocumentService>();

        var setupResult = await setup.EnsureDefaultsAsync(CancellationToken.None);

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

        var unit = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Unit",
            parent_property_id = building.Id,
            unit_no = "101"
        }), CancellationToken.None);

        var lease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
        {
            property_id = unit.Id,
            start_on_utc = "2026-02-01",
            end_on_utc = "2026-02-28",
            rent_amount = "1000.00"
        }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);

        var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
        {
            lease_id = lease.Id,
            received_on_utc = "2026-02-07",
            amount = "100.00"
        }), CancellationToken.None);
        (await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

        var returned = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableReturnedPayment, Payload(new
        {
            original_payment_id = payment.Id,
            returned_on_utc = returnedOnUtc,
            amount = amount
        }), CancellationToken.None);

        return (returned, setupResult.ReceivablesOpenItemsOperationalRegisterId);
    }

    private static async Task<IReadOnlyList<AccountingEntryRow>> ReadAccountingEntriesAsync(IUnitOfWork uow, Guid documentId)
    {
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);
        const string sql = """
SELECT amount AS Amount, is_storno AS IsStorno
FROM accounting_register_main
WHERE document_id = @document_id
ORDER BY entry_id;
""";
        var rows = await uow.Connection.QueryAsync<AccountingEntryRow>(new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: CancellationToken.None));
        return rows.AsList();
    }

    private static async Task<IReadOnlyList<OperationalRegisterMovementQueryReadRow>> ReadMovementsAsync(IOperationalRegisterReadService opregRead, Guid registerId, Guid documentId)
    {
        var page = await opregRead.GetMovementsPageAsync(
            new OperationalRegisterMovementsPageRequest(
                RegisterId: registerId,
                FromInclusive: new DateOnly(2026, 2, 1),
                ToInclusive: new DateOnly(2026, 3, 1),
                DocumentId: documentId,
                PageSize: 50),
            CancellationToken.None);

        return page.Lines;
    }

    private sealed class AccountingEntryRow
    {
        public decimal Amount { get; init; }
        public bool IsStorno { get; init; }
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
        catch { }
    }
}
