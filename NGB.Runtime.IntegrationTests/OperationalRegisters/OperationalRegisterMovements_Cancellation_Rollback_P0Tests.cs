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
using NGB.PostgreSql.OperationalRegisters;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: cancellation must be rollback-safe for operational register movements.
///
/// We simulate cancellation *after* the movements append has executed SQL, but before the transaction is committed.
/// This must rollback:
/// - movements rows
/// - operational_register_write_state begin row
/// - dirty markers (operational_register_finalizations)
/// - has_movements flip (for the first ever movement)
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterMovements_Cancellation_Rollback_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Post_WhenCancelledAfterMovementsAppend_RollsBack_Log_Dirty_Movements_AndHasMovements()
    {
        var code = "rr_cancel_post";
        using var host = CreateHostWithCancellationAfterAppend();

        var trigger = host.Services.GetRequiredService<CancelAfterAppendTrigger>();
        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();

        await SeedRegisterDocumentAndResourcesAsync(host, registerId, code, documentId);

        var movements = new[]
        {
            new OperationalRegisterMovement(
                documentId,
                new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc),
                Guid.Empty,
                new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["amount"] = 1m
                })
        };

        trigger.Arm();

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
                ct: trigger.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            // No log / dirty markers should be committed.
            (await CountWriteLogRowsAsync(uow, registerId, documentId, OperationalRegisterWriteOperation.Post))
                .Should().Be(0);

            (await CountFinalizationRowsAsync(uow, registerId)).Should().Be(0);

            // First append flips has_movements inside the transaction - must rollback to FALSE.
            var hasMovements = await uow.Connection.QuerySingleAsync<bool>(new CommandDefinition(
                "SELECT has_movements FROM operational_registers WHERE register_id = @R;",
                new { R = registerId },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

            hasMovements.Should().BeFalse();

            // The movements table may exist (DDL is transactional), but there must be no rows.
            var movementsTable = OperationalRegisterNaming.MovementsTable(code);

            var exists = await uow.Connection.QuerySingleAsync<bool>(new CommandDefinition(
                "SELECT to_regclass(@T) IS NOT NULL;",
                new { T = movementsTable },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

            if (exists)
            {
                (await CountMovementsAsync(uow, movementsTable, documentId, isStorno: null))
                    .Should().Be(0);
            }
        }
    }

    [Fact]
    public async Task Unpost_WhenCancelledAfterStornoAppend_RollsBack_Log_AndDoesNotAppendStorno()
    {
        var code = "rr_cancel_unpost";
        using var host = CreateHostWithCancellationAfterAppend();

        var trigger = host.Services.GetRequiredService<CancelAfterAppendTrigger>();
        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();

        await SeedRegisterDocumentAndResourcesAsync(host, registerId, code, documentId);

        // Seed a successful Post (no cancellation).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            await applier.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                [
                    new OperationalRegisterMovement(
                        documentId,
                        new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc),
                        Guid.Empty,
                        new Dictionary<string, decimal>(StringComparer.Ordinal)
                        {
                            ["amount"] = 1m
                        })
                ],
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None);
        }

        var movementsTable = OperationalRegisterNaming.MovementsTable(code);

        DateTime? dirtySinceBefore;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            // Baseline: 1 non-storno, 0 storno.
            (await CountMovementsAsync(uow, movementsTable, documentId, isStorno: false)).Should().Be(1);
            (await CountMovementsAsync(uow, movementsTable, documentId, isStorno: true)).Should().Be(0);

            (await CountWriteLogRowsAsync(uow, registerId, documentId, OperationalRegisterWriteOperation.Unpost))
                .Should().Be(0);

            dirtySinceBefore = await uow.Connection.QuerySingleAsync<DateTime?>(new CommandDefinition(
                "SELECT dirty_since_utc FROM operational_register_finalizations WHERE register_id = @R AND period = @M;",
                new { R = registerId, M = new DateOnly(2026, 1, 1) },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

            dirtySinceBefore.Should().NotBeNull();
        }

        trigger.Arm();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            var act = async () => await applier.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Unpost,
                [],
                affectedPeriods: null,
                manageTransaction: true,
                ct: trigger.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            // No storno rows committed.
            (await CountMovementsAsync(uow, movementsTable, documentId, isStorno: false)).Should().Be(1);
            (await CountMovementsAsync(uow, movementsTable, documentId, isStorno: true)).Should().Be(0);

            // No unpost log row committed.
            (await CountWriteLogRowsAsync(uow, registerId, documentId, OperationalRegisterWriteOperation.Unpost))
                .Should().Be(0);

            // Dirty marker must not be updated.
            var dirtySinceAfter = await uow.Connection.QuerySingleAsync<DateTime?>(new CommandDefinition(
                "SELECT dirty_since_utc FROM operational_register_finalizations WHERE register_id = @R AND period = @M;",
                new { R = registerId, M = new DateOnly(2026, 1, 1) },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

            dirtySinceAfter.Should().Be(dirtySinceBefore);
        }
    }

    [Fact]
    public async Task Repost_WhenCancelledAfterStornoAppend_RollsBack_Log_AndDoesNotAppendStornoOrNew()
    {
        var code = "rr_cancel_repost";
        using var host = CreateHostWithCancellationAfterAppend();

        var trigger = host.Services.GetRequiredService<CancelAfterAppendTrigger>();
        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();

        await SeedRegisterDocumentAndResourcesAsync(host, registerId, code, documentId);

        // Seed a successful Post (no cancellation).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            await applier.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                new[]
                {
                    new OperationalRegisterMovement(
                        documentId,
                        new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc),
                        Guid.Empty,
                        new Dictionary<string, decimal>(StringComparer.Ordinal)
                        {
                            ["amount"] = 1m
                        })
                },
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None);
        }

        var movementsTable = OperationalRegisterNaming.MovementsTable(code);

        DateTime? dirtySinceBefore;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            // Baseline: 1 non-storno, 0 storno.
            (await CountMovementsAsync(uow, movementsTable, documentId, isStorno: false)).Should().Be(1);
            (await CountMovementsAsync(uow, movementsTable, documentId, isStorno: true)).Should().Be(0);

            (await CountWriteLogRowsAsync(uow, registerId, documentId, OperationalRegisterWriteOperation.Repost))
                .Should().Be(0);

            dirtySinceBefore = await uow.Connection.QuerySingleAsync<DateTime?>(new CommandDefinition(
                "SELECT dirty_since_utc FROM operational_register_finalizations WHERE register_id = @R AND period = @M;",
                new { R = registerId, M = new DateOnly(2026, 1, 1) },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

            dirtySinceBefore.Should().NotBeNull();
        }

        trigger.Arm();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            var act = async () => await applier.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Repost,
                new[]
                {
                    new OperationalRegisterMovement(
                        documentId,
                        new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
                        Guid.Empty,
                        new Dictionary<string, decimal>(StringComparer.Ordinal)
                        {
                            ["amount"] = 2m
                        })
                },
                affectedPeriods: null,
                manageTransaction: true,
                ct: trigger.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            // No storno/new rows committed.
            (await CountMovementsAsync(uow, movementsTable, documentId, isStorno: false)).Should().Be(1);
            (await CountMovementsAsync(uow, movementsTable, documentId, isStorno: true)).Should().Be(0);

            // No repost log row committed.
            (await CountWriteLogRowsAsync(uow, registerId, documentId, OperationalRegisterWriteOperation.Repost))
                .Should().Be(0);

            // Dirty marker must not be updated.
            var dirtySinceAfter = await uow.Connection.QuerySingleAsync<DateTime?>(new CommandDefinition(
                "SELECT dirty_since_utc FROM operational_register_finalizations WHERE register_id = @R AND period = @M;",
                new { R = registerId, M = new DateOnly(2026, 1, 1) },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

            dirtySinceAfter.Should().Be(dirtySinceBefore);
        }
    }

    private IHost CreateHostWithCancellationAfterAppend()
    {
        // Swap movements store with a wrapper that cancels after the SQL append executed.
        // This provides deterministic coverage of the rollback path.
        return IntegrationHostFactory.Create(
            connectionString: Fixture.ConnectionString,
            configureTestServices: services =>
            {
                services.AddSingleton<CancelAfterAppendTrigger>();
                services.AddScoped<PostgresOperationalRegisterMovementsStore>();
                services.AddScoped<IOperationalRegisterMovementsStore>(sp =>
                    new CancelAfterAppendMovementsStore(
                        sp.GetRequiredService<PostgresOperationalRegisterMovementsStore>(),
                        sp.GetRequiredService<CancelAfterAppendTrigger>()));
            });
    }

    private static async Task SeedRegisterDocumentAndResourcesAsync(
        IHost host,
        Guid registerId,
        string registerCode,
        Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regs = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var resources = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);

        await regs.UpsertAsync(new OperationalRegisterUpsert(registerId, registerCode, "IT Register"), nowUtc, CancellationToken.None);

        await resources.ReplaceAsync(
            registerId,
            new[]
            {
                new OperationalRegisterResourceDefinition("amount", "Amount",1)
            },
            nowUtc,
            CancellationToken.None);

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

    private static async Task<int> CountMovementsAsync(IUnitOfWork uow, string movementsTable, Guid documentId, bool? isStorno)
    {
        var sql = isStorno is null
            ? $"SELECT COUNT(*) FROM {movementsTable} WHERE document_id = @D;"
            : $"SELECT COUNT(*) FROM {movementsTable} WHERE document_id = @D AND is_storno = @S;";

        return await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            sql,
            new { D = documentId, S = isStorno ?? false },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));
    }

    private static async Task<int> CountWriteLogRowsAsync(
        IUnitOfWork uow,
        Guid registerId,
        Guid documentId,
        OperationalRegisterWriteOperation operation)
    {
        return await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM operational_register_write_state WHERE register_id = @R AND document_id = @D AND operation = @O;",
            new { R = registerId, D = documentId, O = (short)operation },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));
    }

    private static async Task<int> CountFinalizationRowsAsync(IUnitOfWork uow, Guid registerId)
    {
        return await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM operational_register_finalizations WHERE register_id = @R;",
            new { R = registerId },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));
    }
}

internal sealed class CancelAfterAppendTrigger
{
    private readonly CancellationTokenSource _cts = new();
    private int _armed;

    public CancellationToken Token => _cts.Token;

    public void Arm() => Interlocked.Exchange(ref _armed, 1);

    public void CancelIfArmed()
    {
        if (Interlocked.Exchange(ref _armed, 0) == 1)
            _cts.Cancel();
    }
}

internal sealed class CancelAfterAppendMovementsStore(
    PostgresOperationalRegisterMovementsStore inner,
    CancelAfterAppendTrigger trigger)
    : IOperationalRegisterMovementsStore
{
    public Task EnsureSchemaAsync(Guid registerId, CancellationToken ct = default)
        => inner.EnsureSchemaAsync(registerId, ct);

    public async Task AppendAsync(Guid registerId, IReadOnlyList<OperationalRegisterMovement> movements, CancellationToken ct = default)
    {
        // Ensure the SQL append executes even if ct is cancelled later.
        await inner.AppendAsync(registerId, movements, CancellationToken.None);
        trigger.CancelIfArmed();
        ct.ThrowIfCancellationRequested();
    }

    public async Task AppendStornoByDocumentAsync(Guid registerId, Guid documentId, CancellationToken ct = default)
    {
        // Ensure the SQL append executes even if ct is cancelled later.
        await inner.AppendStornoByDocumentAsync(registerId, documentId, CancellationToken.None);
        trigger.CancelIfArmed();
        ct.ThrowIfCancellationRequested();
    }
}
