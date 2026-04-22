using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: strict resource mapping. A single unknown resource key must fail-fast and roll back the whole write.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterMovements_UnknownResourceKey_Rollback_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Post_WithUnknownResourceColumn_Throws_AndDoesNotCommit_LogDirtyOrRows()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();
        var code = "reskey_strict_" + Guid.CreateVersion7().ToString("N")[..8];

        await SeedRegisterAndDocumentAsync(host, registerId, code, documentId, resources: new[]
        {
            new OperationalRegisterResourceDefinition("amount", "Amount", 1)
        });

        var badMovements = new[]
        {
            new OperationalRegisterMovement(
                documentId,
                new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc),
                Guid.Empty,
                new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["amount"] = 1m,
                    ["oops"] = 123m
                })
        };

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            var act = async () => await applier.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                badMovements,
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None);

            var ex = await act.Should().ThrowAsync<OperationalRegisterResourcesValidationException>();
            ex.Which.RegisterId.Should().Be(registerId);
            ex.Which.Reason.Should().Contain("Unknown resource column 'oops'");

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

            logCount.Should().Be(0);

            var finCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                "SELECT count(*) FROM operational_register_finalizations WHERE register_id = @R;",
                new { R = registerId },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

            finCount.Should().Be(0);

            var table = OperationalRegisterNaming.MovementsTable(code);

            var exists = await uow.Connection.QuerySingleAsync<bool>(new CommandDefinition(
                "SELECT to_regclass(@T) IS NOT NULL;",
                new { T = table },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

            // Schema objects are allowed to exist even when the write itself fails.
            // Atomicity requirement here is: log + dirty markers + rows are not committed.
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
            new OperationalRegisterUpsert(registerId, registerCode, "Strict Resources Test"),
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
