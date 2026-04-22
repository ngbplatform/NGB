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
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Runtime;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmRentCharge_PostAfterUnpost_PostsAgain_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmRentCharge_PostAfterUnpost_PostsAgain_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

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
            var trialBalance = scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var seeded = await SeedDraftRentChargeAsync(scope.ServiceProvider);

            (await documents.PostAsync(PropertyManagementCodes.RentCharge, seeded.RentCharge.Id, CancellationToken.None))
                .Status.Should().Be(DocumentStatus.Posted);

            (await documents.UnpostAsync(PropertyManagementCodes.RentCharge, seeded.RentCharge.Id, CancellationToken.None))
                .Status.Should().Be(DocumentStatus.Draft);

            (await documents.PostAsync(PropertyManagementCodes.RentCharge, seeded.RentCharge.Id, CancellationToken.None))
                .Status.Should().Be(DocumentStatus.Posted);

            (await documents.UnpostAsync(PropertyManagementCodes.RentCharge, seeded.RentCharge.Id, CancellationToken.None))
                .Status.Should().Be(DocumentStatus.Draft);

            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            const string sql = """
SELECT amount AS Amount, is_storno AS IsStorno
FROM accounting_register_main
WHERE document_id = @document_id
ORDER BY entry_id;
""";

            var rows = (await uow.Connection.QueryAsync<AccountingEntryRow>(
                new CommandDefinition(sql, new { document_id = seeded.RentCharge.Id }, uow.Transaction, cancellationToken: CancellationToken.None)))
                .AsList();

            // Accounting now reverses only the current posted snapshot on the second Unpost.
            // For Post -> Unpost -> Post -> Unpost this yields 4 rows (2 business posts + 2 storno rows).
            rows.Should().HaveCount(4);
            rows.Count(x => !x.IsStorno).Should().Be(2);
            rows.Count(x => x.IsStorno).Should().Be(2);

            var tb = await trialBalance.GetAsync(
                fromInclusive: new DateOnly(2026, 2, 1),
                toInclusive: new DateOnly(2026, 2, 1),
                ct: CancellationToken.None);

            tb.Should().ContainSingle(r => r.AccountCode == "1100" && r.ClosingBalance == 0m);
            tb.Should().ContainSingle(r => r.AccountCode == "4000" && r.ClosingBalance == 0m);

            var movements = await opregRead.GetMovementsPageAsync(
                new OperationalRegisterMovementsPageRequest(
                    RegisterId: seeded.RegisterId,
                    FromInclusive: new DateOnly(2026, 2, 1),
                    ToInclusive: new DateOnly(2026, 3, 1),
                    DocumentId: seeded.RentCharge.Id,
                    PageSize: 50),
                CancellationToken.None);

            movements.Lines.Should().HaveCount(6);

            var balances = await opregRead.GetBalancesPageAsync(
                new OperationalRegisterMonthlyProjectionPageRequest(
                    RegisterId: seeded.RegisterId,
                    FromInclusive: new DateOnly(2026, 2, 1),
                    ToInclusive: new DateOnly(2026, 2, 1),
                    PageSize: 50),
                CancellationToken.None);

            balances.Lines.Sum(x => x.Values.TryGetValue("amount", out var amount) ? amount : 0m).Should().Be(0m);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    private static async Task<(DocumentDto RentCharge, Guid RegisterId)> SeedDraftRentChargeAsync(IServiceProvider services)
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

        var property = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Unit",
            parent_property_id = building.Id,
            unit_no = "101"
        }), CancellationToken.None);

        var lease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
        {
            property_id = property.Id,
            start_on_utc = "2026-02-01",
            end_on_utc = "2026-02-28",
            rent_amount = "1000.00"
        }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);

        var rentCharge = await documents.CreateDraftAsync(PropertyManagementCodes.RentCharge, Payload(new
        {
            lease_id = lease.Id,
            period_from_utc = "2026-02-01",
            period_to_utc = "2026-02-28",
            due_on_utc = "2026-02-05",
            amount = "123.45",
            memo = "Rent"
        }), CancellationToken.None);

        return (rentCharge, setupResult.ReceivablesOpenItemsOperationalRegisterId);
    }

    private static RecordPayload Payload(object obj, IReadOnlyDictionary<string, RecordPartPayload>? parts = null)
    {
        var el = JsonSerializer.SerializeToElement(obj);
        var dict = new Dictionary<string, JsonElement>();
        foreach (var p in el.EnumerateObject())
            dict[p.Name] = p.Value;
        return new RecordPayload(dict, parts);
    }

    private sealed record AccountingEntryRow(decimal Amount, bool IsStorno);

    private static async Task DisposeFactoryAsync(PmApiFactory factory)
    {
        await factory.DisposeAsync();
        factory.Dispose();
    }
}
