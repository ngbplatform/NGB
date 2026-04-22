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
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: Guardrails against non-UTC timestamps in operational register movements.
/// We require DateTimeKind.Utc at the boundary to prevent subtle month drift (TimeZone-dependent period_month).
///
/// Note: per-register tables (opreg_*__movements) are created dynamically and are NOT always dropped by Respawn.
/// Therefore, tests MUST avoid using a constant register code (table name) if they assert table existence.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterMovements_UtcEnforcement_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Post_WhenOccurredAtUtcIsNotUtc_ThrowsEarly_AndDoesNotCreateSideEffects()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var registerId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        // Use a unique code per test to avoid flakiness due to dynamically created per-register tables.
        var code = "rr_" + Guid.NewGuid().ToString("N")[..8];

        await SeedRegisterAndDocumentAsync(host, registerId, code, documentId, resources: new[]
        {
            new OperationalRegisterResourceDefinition("amount", "Amount", 1)
        });

        var movements = new[]
        {
            new OperationalRegisterMovement(documentId,
                new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Unspecified),
                Guid.Empty,
                new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 10m })
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

            var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
            ex.Which.ParamName.Should().EndWith("OccurredAtUtc");
            ex.Which.Message.Should().Contain("must be UTC");
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var table = OperationalRegisterNaming.MovementsTable(code);

            var exists = await uow.Connection.QuerySingleAsync<bool>(new CommandDefinition(
                "SELECT to_regclass(@T) IS NOT NULL;",
                new { T = $"public.{table}" },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

            exists.Should().BeFalse("UTC validation must occur before EnsureSchema and before any write pipeline steps");

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

            var dirtyCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                """
                SELECT count(*)
                FROM operational_register_finalizations
                WHERE register_id = @R;
                """,
                new { R = registerId },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

            dirtyCount.Should().Be(0);
        }
    }

    [Fact]
    public async Task MovementsStore_Append_WhenOccurredAtUtcIsNotUtc_Throws_AndDoesNotInsertRows()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var registerId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var code = "rr_" + Guid.NewGuid().ToString("N")[..8];

        await SeedRegisterAndDocumentAsync(host, registerId, code, documentId, resources: new[]
        {
            new OperationalRegisterResourceDefinition("amount", "Amount", 1)
        });

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var store = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>();

            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            // Ensure schema outside the transaction (table must exist to validate "no inserts").
            await store.EnsureSchemaAsync(registerId, CancellationToken.None);

            await uow.BeginTransactionAsync(CancellationToken.None);

            var movements = new[]
            {
                new OperationalRegisterMovement(documentId,
                    new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Local),
                    Guid.Empty,
                    new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 10m })
            };

            var act = async () => await store.AppendAsync(registerId, movements, CancellationToken.None);

            var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
            ex.Which.ParamName.Should().EndWith("OccurredAtUtc");
            ex.Which.Message.Should().Contain("must be UTC");

            await uow.RollbackAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var table = OperationalRegisterNaming.MovementsTable(code);

            var count = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                "SELECT count(*) FROM " + table + " WHERE document_id = @D;",
                new { D = documentId },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

            count.Should().Be(0);
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
            new OperationalRegisterUpsert(registerId, registerCode, "Rent Roll"),
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
