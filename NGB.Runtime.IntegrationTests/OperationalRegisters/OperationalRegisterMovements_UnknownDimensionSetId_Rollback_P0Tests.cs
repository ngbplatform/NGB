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
/// P0: DimensionSetId is a FK to platform_dimension_sets.
/// If a caller passes a non-existent DimensionSetId (not created via DimensionSetService),
/// the write must fail and roll back atomically: no write_log, no dirty markers, no movements,
/// and register metadata must remain unchanged.
///
/// Note: IDimensionSetReader is defensive and may resolve unknown ids to an empty bag;
/// therefore this test focuses on DB-level FK enforcement + transactional rollback.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterMovements_UnknownDimensionSetId_Rollback_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Post_WithUnknownDimensionSetId_ThrowsFkViolation_AndDoesNotCommit_LogDirtyOrRows()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();
        var code = "dimset_fk_" + Guid.CreateVersion7().ToString("N")[..8];

        await SeedRegisterAndDocumentAsync(host, registerId, code, documentId, resources: new[]
        {
            new OperationalRegisterResourceDefinition("amount", "Amount", 1)
        });

        var unknownSetId = Guid.CreateVersion7();

        var movements = new[]
        {
            new OperationalRegisterMovement(
                documentId,
                new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc),
                unknownSetId,
                new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["amount"] = 1m
                })
        };

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            var act = async () => await applier.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                movements,
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None);

            var ex = await act.Should().ThrowAsync<PostgresException>();
            ex.Which.SqlState.Should().Be("23503", "dimension_set_id must be enforced as a FK");
            ex.Which.ConstraintName.Should().NotBeNull();
            ex.Which.ConstraintName!.Should().EndWith("_dimension_set_id_fkey");
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var logCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                """
                SELECT count(*)
                FROM operational_register_write_state
                WHERE register_id = @R AND document_id = @D AND operation = @O;
                """,
                new { R = registerId, D = documentId, O = (short)OperationalRegisterWriteOperation.Post },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

            logCount.Should().Be(0, "write_log must roll back on FK failure");

            var finCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                "SELECT count(*) FROM operational_register_finalizations WHERE register_id = @R;",
                new { R = registerId },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

            finCount.Should().Be(0, "dirty markers must not be committed on FK failure");

            var hasMovements = await uow.Connection.QuerySingleAsync<bool>(new CommandDefinition(
                "SELECT has_movements FROM operational_registers WHERE register_id = @R;",
                new { R = registerId },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

            hasMovements.Should().BeFalse("register.has_movements update must roll back on FK failure");

            var table = OperationalRegisterNaming.MovementsTable(code);

            var exists = await uow.Connection.QuerySingleAsync<bool>(new CommandDefinition(
                "SELECT to_regclass(@T) IS NOT NULL;",
                new { T = table },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

            // Schema objects may or may not exist depending on where the failure occurred.
            // Atomicity requirement is: no movement rows are committed.
            if (exists)
            {
                var rowsCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                    $"SELECT count(*) FROM {table} WHERE document_id = @D;",
                    new { D = documentId },
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));

                rowsCount.Should().Be(0);
            }
        }
    }

    private static async Task SeedRegisterAndDocumentAsync(
        IHost host,
        Guid registerId,
        string registerCode,
        Guid documentId,
        IReadOnlyList<OperationalRegisterResourceDefinition>? resources = null)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var resRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);

        await regRepo.UpsertAsync(
            new OperationalRegisterUpsert(registerId, registerCode, "Unknown DimensionSet FK Test"),
            nowUtc,
            CancellationToken.None);

        if (resources is not null)
            await resRepo.ReplaceAsync(registerId, resources, nowUtc, CancellationToken.None);

        await docs.CreateAsync(new DocumentRecord
        {
            Id = documentId,
            TypeCode = "it_doc",
            Number = null,
            DateUtc = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            Status = DocumentStatus.Draft,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            PostedAtUtc = null,
            MarkedForDeletionAtUtc = null
        }, CancellationToken.None);

        await uow.CommitAsync(CancellationToken.None);
    }
}
