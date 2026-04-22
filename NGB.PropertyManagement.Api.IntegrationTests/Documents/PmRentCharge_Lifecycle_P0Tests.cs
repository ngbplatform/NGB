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
using NGB.Runtime.Documents.Workflow;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmRentCharge_Lifecycle_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmRentCharge_Lifecycle_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UnpostAsync_WritesSingleStorno_ForAccountingAndOperationalRegister()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
            var opregRead = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var seeded = await SeedPostedRentChargeAsync(scope.ServiceProvider, dueOnUtc: "2026-02-05", amount: "123.45");

            var unposted = await documents.UnpostAsync(PropertyManagementCodes.RentCharge, seeded.RentCharge.Id, CancellationToken.None);
            unposted.Status.Should().Be(DocumentStatus.Draft);

            var accounting = await ReadAccountingEntriesAsync(uow, seeded.RentCharge.Id);
            accounting.Should().HaveCount(2);
            accounting.Count(x => x.IsStorno).Should().Be(1);
            accounting.Sum(x => x.IsStorno ? -x.Amount : x.Amount).Should().Be(0m);

            var movements = await ReadMovementsAsync(opregRead, seeded.RegisterId, seeded.RentCharge.Id);
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
    public async Task RepostAsync_AppendsStornoAndFreshRows_ForAccountingAndOperationalRegister()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
            var opregRead = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var seeded = await SeedPostedRentChargeAsync(scope.ServiceProvider, dueOnUtc: "2026-02-05", amount: "123.45");

            var reposted = await documents.RepostAsync(PropertyManagementCodes.RentCharge, seeded.RentCharge.Id, CancellationToken.None);
            reposted.Status.Should().Be(DocumentStatus.Posted);

            var accounting = await ReadAccountingEntriesAsync(uow, seeded.RentCharge.Id);
            accounting.Should().HaveCount(3);
            accounting.Count(x => x.IsStorno).Should().Be(1);
            accounting.Count(x => !x.IsStorno).Should().Be(2);
            accounting.Sum(x => x.IsStorno ? -x.Amount : x.Amount).Should().Be(123.45m);

            var movements = await ReadMovementsAsync(opregRead, seeded.RegisterId, seeded.RentCharge.Id);
            movements.Should().HaveCount(3);
            movements.Count(x => x.IsStorno).Should().Be(1);
            movements.Count(x => !x.IsStorno).Should().Be(2);
            movements.Sum(x => x.IsStorno ? -Convert.ToDecimal(x.Values["amount"]) : Convert.ToDecimal(x.Values["amount"])).Should().Be(123.45m);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task PostUnpostPost_RealCycle_AppendsFreshRows_AndReturnsPosted()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
            var opregRead = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var seeded = await SeedPostedRentChargeAsync(scope.ServiceProvider, dueOnUtc: "2026-02-05", amount: "123.45");

            (await documents.UnpostAsync(PropertyManagementCodes.RentCharge, seeded.RentCharge.Id, CancellationToken.None))
                .Status.Should().Be(DocumentStatus.Draft);

            var repostedByPost = await documents.PostAsync(PropertyManagementCodes.RentCharge, seeded.RentCharge.Id, CancellationToken.None);
            repostedByPost.Status.Should().Be(DocumentStatus.Posted);

            var accounting = await ReadAccountingEntriesAsync(uow, seeded.RentCharge.Id);
            accounting.Should().HaveCount(3);
            accounting.Count(x => x.IsStorno).Should().Be(1);
            accounting.Sum(x => x.IsStorno ? -x.Amount : x.Amount).Should().Be(123.45m);

            var movements = await ReadMovementsAsync(opregRead, seeded.RegisterId, seeded.RentCharge.Id);
            movements.Should().HaveCount(3);
            movements.Count(x => x.IsStorno).Should().Be(1);
            movements.Sum(x => x.IsStorno ? -Convert.ToDecimal(x.Values["amount"]) : Convert.ToDecimal(x.Values["amount"])).Should().Be(123.45m);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task PostUnpostThenRepost_WhenDraft_ThrowsStateMismatch_AndDoesNotAppendFreshRows()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
            var opregRead = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var seeded = await SeedPostedRentChargeAsync(scope.ServiceProvider, dueOnUtc: "2026-02-05", amount: "123.45");

            (await documents.UnpostAsync(PropertyManagementCodes.RentCharge, seeded.RentCharge.Id, CancellationToken.None))
                .Status.Should().Be(DocumentStatus.Draft);

            var act = () => documents.RepostAsync(PropertyManagementCodes.RentCharge, seeded.RentCharge.Id, CancellationToken.None);
            await act.Should().ThrowAsync<DocumentWorkflowStateMismatchException>();

            var current = await documents.GetByIdAsync(PropertyManagementCodes.RentCharge, seeded.RentCharge.Id, CancellationToken.None);
            current.Status.Should().Be(DocumentStatus.Draft);

            var accounting = await ReadAccountingEntriesAsync(uow, seeded.RentCharge.Id);
            accounting.Should().HaveCount(2);
            accounting.Count(x => x.IsStorno).Should().Be(1);
            accounting.Sum(x => x.IsStorno ? -x.Amount : x.Amount).Should().Be(0m);

            var movements = await ReadMovementsAsync(opregRead, seeded.RegisterId, seeded.RentCharge.Id);
            movements.Should().HaveCount(2);
            movements.Count(x => x.IsStorno).Should().Be(1);
            movements.Sum(x => x.IsStorno ? -Convert.ToDecimal(x.Values["amount"]) : Convert.ToDecimal(x.Values["amount"])).Should().Be(0m);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Theory]
    [InlineData("unpost")]
    [InlineData("repost")]
    public async Task ClosedPeriod_Rejects_Unpost_And_Repost_WithoutSideEffects(string operation)
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
            var opregRead = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();
            var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var seeded = await SeedPostedRentChargeAsync(scope.ServiceProvider, dueOnUtc: "2026-02-05", amount: "123.45");

            await closing.CloseMonthAsync(new DateOnly(2026, 2, 1), closedBy: "tests", CancellationToken.None);

            Func<Task> act = operation switch
            {
                "unpost" => () => documents.UnpostAsync(PropertyManagementCodes.RentCharge, seeded.RentCharge.Id, CancellationToken.None),
                "repost" => () => documents.RepostAsync(PropertyManagementCodes.RentCharge, seeded.RentCharge.Id, CancellationToken.None),
                _ => throw new InvalidOperationException($"Unknown operation '{operation}'.")
            };

            var ex = await act.Should().ThrowAsync<PostingPeriodClosedException>();
            ex.Which.ErrorCode.Should().Be(PostingPeriodClosedException.ErrorCodeConst);

            var reloaded = await documents.GetByIdAsync(PropertyManagementCodes.RentCharge, seeded.RentCharge.Id, CancellationToken.None);
            reloaded.Status.Should().Be(DocumentStatus.Posted);

            var accounting = await ReadAccountingEntriesAsync(uow, seeded.RentCharge.Id);
            accounting.Should().HaveCount(1);
            accounting.Count(x => x.IsStorno).Should().Be(0);

            var movements = await ReadMovementsAsync(opregRead, seeded.RegisterId, seeded.RentCharge.Id);
            movements.Should().HaveCount(1);
            movements.Count(x => x.IsStorno).Should().Be(0);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    private static async Task<(DocumentDto RentCharge, Guid RegisterId)> SeedPostedRentChargeAsync(IServiceProvider services, string dueOnUtc, string amount)
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

        var rentCharge = await documents.CreateDraftAsync(PropertyManagementCodes.RentCharge, Payload(new
        {
            lease_id = lease.Id,
            period_from_utc = "2026-02-01",
            period_to_utc = "2026-02-28",
            due_on_utc = dueOnUtc,
            amount = amount,
            memo = "Rent"
        }), CancellationToken.None);

        var posted = await documents.PostAsync(PropertyManagementCodes.RentCharge, rentCharge.Id, CancellationToken.None);
        posted.Status.Should().Be(DocumentStatus.Posted);

        return (posted, setupResult.ReceivablesOpenItemsOperationalRegisterId);
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

        var rows = await uow.Connection.QueryAsync<AccountingEntryRow>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: CancellationToken.None));

        return rows.AsList();
    }

    private static async Task<IReadOnlyList<OperationalRegisterMovementQueryReadRow>> ReadMovementsAsync(
        IOperationalRegisterReadService opregRead,
        Guid registerId,
        Guid documentId)
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
        catch { /* ignore */ }
    }
}
