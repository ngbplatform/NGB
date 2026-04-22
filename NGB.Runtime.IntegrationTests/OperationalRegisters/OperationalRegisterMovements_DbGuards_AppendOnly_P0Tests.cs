using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: per-register movements tables are append-only.
/// UPDATE/DELETE must be blocked by the shared append-only guard trigger.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterMovements_DbGuards_AppendOnly_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task UpdateAndDeleteAreForbiddenOnMovementsTable()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerCode = "rr_" + Guid.CreateVersion7().ToString("N")[..8];
        var registerId = await CreateRegisterAsync(host, registerCode);
        await ConfigureResourcesAsync(host, registerId);

        var docId = Guid.CreateVersion7();
        await SeedDraftDocAsync(host, docId);

        await ApplyOneMovementAsync(host, registerId, docId);

        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regs = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();

        await uow.EnsureConnectionOpenAsync(CancellationToken.None);
        var reg = await regs.GetByIdAsync(registerId, CancellationToken.None);
        reg.Should().NotBeNull();

        var movementsTable = OperationalRegisterNaming.MovementsTable(reg!.TableCode);

        var movementId = await uow.Connection.QuerySingleAsync<long>(
            $"SELECT movement_id FROM {movementsTable} WHERE document_id = @D ORDER BY movement_id LIMIT 1;",
            new { D = docId },
            transaction: uow.Transaction);

        // UPDATE must be blocked.
        {
            var ex = await FluentActions
                .Invoking(() => uow.Connection.ExecuteAsync(
                    new CommandDefinition(
                        $"UPDATE {movementsTable} SET amount = amount + 1 WHERE movement_id = @M;",
                        new { M = movementId },
                        transaction: uow.Transaction)))
                .Should().ThrowAsync<PostgresException>();

            ex.Which.SqlState.Should().Be("55000");
            ex.Which.Message.Should().Contain("Append-only table cannot be mutated");
            ex.Which.Message.Should().Contain(movementsTable);
        }

        // DELETE must be blocked.
        {
            var ex = await FluentActions
                .Invoking(() => uow.Connection.ExecuteAsync(
                    new CommandDefinition(
                        $"DELETE FROM {movementsTable} WHERE movement_id = @M;",
                        new { M = movementId },
                        transaction: uow.Transaction)))
                .Should().ThrowAsync<PostgresException>();

            ex.Which.SqlState.Should().Be("55000");
            ex.Which.Message.Should().Contain("Append-only table cannot be mutated");
            ex.Which.Message.Should().Contain(movementsTable);
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

        var nowUtc = new DateTime(2026, 1, 7, 12, 0, 0, DateTimeKind.Utc);
        var dateUtc = new DateTime(2026, 1, 7, 0, 0, 0, DateTimeKind.Utc);

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
            OccurredAtUtc: new DateTime(2026, 1, 7, 0, 0, 0, DateTimeKind.Utc),
            DimensionSetId: Guid.Empty,
            Resources: new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["amount"] = 10m
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
