using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: operational_registers metadata becomes partially immutable after any movements exist.
/// The DB guard must protect:
/// - DELETE (entire row)
/// - code/code_norm/table_code identity
/// - has_movements cannot flip back
/// ...while still allowing benign updates like name edits.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterMetadata_DbGuards_AfterMovements_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AfterMovementsExist_DeleteAndIdentityUpdatesAreForbidden_NameUpdateIsAllowed()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerCode = "rr_" + Guid.CreateVersion7().ToString("N")[..8];
        var registerId = await CreateRegisterAsync(host, registerCode);
        await ConfigureResourcesAsync(host, registerId);

        var docId = Guid.CreateVersion7();
        await SeedDraftDocAsync(host, docId);

        await ApplyOneMovementAsync(host, registerId, docId);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        // Sanity: has_movements is now TRUE.
        (await conn.QuerySingleAsync<bool>(
            "SELECT has_movements FROM operational_registers WHERE register_id = @R;",
            new { R = registerId }))
            .Should().BeTrue();

        // 1) Name update is allowed.
        await conn.ExecuteAsync(
            "UPDATE operational_registers SET name = @N WHERE register_id = @R;",
            new { R = registerId, N = "Renamed" });

        (await conn.QuerySingleAsync<string>(
            "SELECT name FROM operational_registers WHERE register_id = @R;",
            new { R = registerId }))
            .Should().Be("Renamed");

        // 2) Code update is forbidden (identity mutation).
        {
            var ex = await FluentActions
                .Invoking(() => conn.ExecuteAsync(
                    "UPDATE operational_registers SET code = @C WHERE register_id = @R;",
                    new { R = registerId, C = registerCode + "_x" }))
                .Should().ThrowAsync<PostgresException>();

            ex.Which.SqlState.Should().Be("P0001");
            ex.Which.Message.Should().Contain("Operational register code is immutable after movements exist.");
        }

        // 3) has_movements cannot flip back.
        {
            var ex = await FluentActions
                .Invoking(() => conn.ExecuteAsync(
                    "UPDATE operational_registers SET has_movements = FALSE WHERE register_id = @R;",
                    new { R = registerId }))
                .Should().ThrowAsync<PostgresException>();

            ex.Which.SqlState.Should().Be("P0001");
            ex.Which.Message.Should().Contain("Operational register has_movements can never flip back");
        }

        // 4) Delete is forbidden.
        {
            var ex = await FluentActions
                .Invoking(() => conn.ExecuteAsync(
                    "DELETE FROM operational_registers WHERE register_id = @R;",
                    new { R = registerId }))
                .Should().ThrowAsync<PostgresException>();

            ex.Which.SqlState.Should().Be("P0001");
            ex.Which.Message.Should().Contain("Operational register metadata is immutable after movements exist.");
        }
    }

    private static async Task<Guid> CreateRegisterAsync(IHost host, string code)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
        return await mgmt.UpsertAsync(code, name: "IT Register", CancellationToken.None);
    }

    private static async Task ConfigureResourcesAsync(IHost host, Guid registerId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();

        await mgmt.ReplaceResourcesAsync(registerId,
            [
                new OperationalRegisterResourceDefinition("amount", "Amount", 1)
            ],
            CancellationToken.None);
    }

    private static async Task SeedDraftDocAsync(IHost host, Guid id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var nowUtc = new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);
        var dateUtc = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.CreateAsync(new DocumentRecord
            {
                Id = id,
                TypeCode = "it_doc",
                Number = null,
                DateUtc = dateUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);
        }, CancellationToken.None);
    }

    private static async Task ApplyOneMovementAsync(IHost host, Guid registerId, Guid docId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

        var movement = new OperationalRegisterMovement(
            DocumentId: docId,
            OccurredAtUtc: new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            DimensionSetId: Guid.Empty,
            Resources: new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["amount"] = 1m
            });

        await applier.ApplyMovementsForDocumentAsync(
            registerId,
            docId,
            OperationalRegisterWriteOperation.Post,
            [movement],
            affectedPeriods: null,
            manageTransaction: true,
            ct: CancellationToken.None);
    }
}
