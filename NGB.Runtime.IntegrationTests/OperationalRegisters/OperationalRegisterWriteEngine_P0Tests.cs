using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.OperationalRegisters.Exceptions;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: End-to-end semantics for the canonical operational register write pipeline:
/// - idempotency via operational_register_write_state
/// - marking affected months as Dirty in operational_register_finalizations
/// - atomicity (rollback must undo log + dirty markers)
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterWriteEngine_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Execute_WhenBegun_ExecutesAction_MarksMonthsDirty_AndCompletesLog()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();

        await SeedRegisterAndDocumentAsync(host, registerId, documentId);

        var executed = 0;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<IOperationalRegisterWriteEngine>();

            var result = await engine.ExecuteAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                affectedPeriods:
                [
                    new DateOnly(2026, 1, 15), // mid-month -> must normalize to month start
                    new DateOnly(2026, 2, 1)
                ],
                writeAction: _ =>
                {
                    executed++;
                    return Task.CompletedTask;
                },
                manageTransaction: true,
                ct: CancellationToken.None);

            result.Should().Be(OperationalRegisterWriteResult.Executed);
            executed.Should().Be(1);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var finalizations = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            var jan = await finalizations.GetAsync(registerId, new DateOnly(2026, 1, 1), CancellationToken.None);
            var feb = await finalizations.GetAsync(registerId, new DateOnly(2026, 2, 1), CancellationToken.None);

            jan.Should().NotBeNull();
            jan!.Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty);
            jan.DirtySinceUtc.Should().NotBeNull();
            jan.FinalizedAtUtc.Should().BeNull();

            feb.Should().NotBeNull();
            feb!.Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty);
            feb.DirtySinceUtc.Should().NotBeNull();
            feb.FinalizedAtUtc.Should().BeNull();

            await uow.EnsureConnectionOpenAsync(CancellationToken.None);
            var completedAtUtc = await uow.Connection.QuerySingleOrDefaultAsync<DateTime?>(
                new CommandDefinition(
                    """
                    SELECT completed_at_utc
                    FROM operational_register_write_state
                    WHERE register_id = @R AND document_id = @D AND operation = @O;
                    """,
                    new { R = registerId, D = documentId, O = (short)OperationalRegisterWriteOperation.Post },
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));

            completedAtUtc.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Execute_WhenAlreadyCompleted_IsStrictlyIdempotent_AndDoesNotInvokeAction()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();

        await SeedRegisterAndDocumentAsync(host, registerId, documentId);

        var executed = 0;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<IOperationalRegisterWriteEngine>();

            var r1 = await engine.ExecuteAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                affectedPeriods: new[] { new DateOnly(2026, 1, 5) },
                writeAction: _ =>
                {
                    executed++;
                    return Task.CompletedTask;
                },
                manageTransaction: true,
                ct: CancellationToken.None);

            var r2 = await engine.ExecuteAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                affectedPeriods: new[] { new DateOnly(2026, 1, 25) }, // should not matter
                writeAction: _ =>
                {
                    executed++;
                    return Task.CompletedTask;
                },
                manageTransaction: true,
                ct: CancellationToken.None);

            r1.Should().Be(OperationalRegisterWriteResult.Executed);
            r2.Should().Be(OperationalRegisterWriteResult.AlreadyCompleted);
            executed.Should().Be(1);
        }
    }

    [Fact]
    public async Task Execute_WhenExistingInProgressIsNotStale_Throws_AndDoesNotInvokeAction()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();

        await SeedRegisterAndDocumentAsync(host, registerId, documentId);

        var startedAtUtc = DateTime.UtcNow;

        // Seed an in-progress log row (simulate another worker).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            await uow.Connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO operational_register_write_state(register_id, document_id, operation, started_at_utc, completed_at_utc)
                VALUES (@R, @D, @O, @S, NULL);
                """,
                new
                {
                    R = registerId,
                    D = documentId,
                    O = (short)OperationalRegisterWriteOperation.Post,
                    S = startedAtUtc
                },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));
            await uow.CommitAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<IOperationalRegisterWriteEngine>();

            var executed = 0;

            var act = async () => await engine.ExecuteAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                affectedPeriods: new[] { new DateOnly(2026, 1, 1) },
                writeAction: _ =>
                {
                    executed++;
                    return Task.CompletedTask;
                },
                manageTransaction: true,
                ct: CancellationToken.None);

            await act.Should().ThrowAsync<OperationalRegisterWriteAlreadyInProgressException>();

            executed.Should().Be(0);
        }
    }

    [Fact]
    public async Task Execute_WhenExistingInProgressIsStale_TakesOver_Executes_AndCompletesLog()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();

        await SeedRegisterAndDocumentAsync(host, registerId, documentId);

        // Seed a stale in-progress log row (started_at older than 10 minutes).
        var staleStartedAtUtc = DateTime.UtcNow.AddMinutes(-11);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            await uow.Connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO operational_register_write_state(register_id, document_id, operation, started_at_utc, completed_at_utc)
                VALUES (@R, @D, @O, @S, NULL);
                """,
                new
                {
                    R = registerId,
                    D = documentId,
                    O = (short)OperationalRegisterWriteOperation.Post,
                    S = staleStartedAtUtc
                },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));
            await uow.CommitAsync(CancellationToken.None);
        }

        var executed = 0;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<IOperationalRegisterWriteEngine>();

            var result = await engine.ExecuteAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                affectedPeriods: new[] { new DateOnly(2026, 1, 20) },
                writeAction: _ =>
                {
                    executed++;
                    return Task.CompletedTask;
                },
                manageTransaction: true,
                ct: CancellationToken.None);

            result.Should().Be(OperationalRegisterWriteResult.Executed);
            executed.Should().Be(1);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var row = await uow.Connection.QuerySingleAsync<(DateTime StartedAtUtc, DateTime? CompletedAtUtc)>(
                new CommandDefinition(
                    """
                    SELECT started_at_utc AS StartedAtUtc, completed_at_utc AS CompletedAtUtc
                    FROM operational_register_write_state
                    WHERE register_id = @R AND document_id = @D AND operation = @O;
                    """,
                    new { R = registerId, D = documentId, O = (short)OperationalRegisterWriteOperation.Post },
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));

            row.CompletedAtUtc.Should().NotBeNull();
            // Takeover must update started_at_utc away from the stale value.
            row.StartedAtUtc.Should().BeAfter(staleStartedAtUtc);
        }
    }

    [Fact]
    public async Task Execute_WhenWriteActionThrows_RollsBack_LogAndDirtyMarkers()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();

        await SeedRegisterAndDocumentAsync(host, registerId, documentId);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<IOperationalRegisterWriteEngine>();

            var act = async () => await engine.ExecuteAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                affectedPeriods: new[] { new DateOnly(2026, 1, 15) },
                writeAction: _ => throw new NotSupportedException("boom"),
                manageTransaction: true,
                ct: CancellationToken.None);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("*boom*");
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var finalizations = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var logCount = await uow.Connection.QuerySingleAsync<int>(
                new CommandDefinition(
                    """
                    SELECT COUNT(*)::int
                    FROM operational_register_write_state
                    WHERE register_id = @R AND document_id = @D AND operation = @O;
                    """,
                    new { R = registerId, D = documentId, O = (short)OperationalRegisterWriteOperation.Post },
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));

            logCount.Should().Be(0);

            var jan = await finalizations.GetAsync(registerId, new DateOnly(2026, 1, 1), CancellationToken.None);
            jan.Should().BeNull();
        }
    }

    [Fact]
    public async Task Execute_WhenAffectedPeriodsEmpty_DoesNotCreateDirtyMarkers_ButCompletesLog()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();

        await SeedRegisterAndDocumentAsync(host, registerId, documentId);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<IOperationalRegisterWriteEngine>();

            var result = await engine.ExecuteAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                affectedPeriods: Array.Empty<DateOnly>(),
                writeAction: _ => Task.CompletedTask,
                manageTransaction: true,
                ct: CancellationToken.None);

            result.Should().Be(OperationalRegisterWriteResult.Executed);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var finalizationCount = await uow.Connection.QuerySingleAsync<int>(
                new CommandDefinition(
                    "SELECT COUNT(*)::int FROM operational_register_finalizations WHERE register_id = @R;",
                    new { R = registerId },
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));

            finalizationCount.Should().Be(0);

            var completedAtUtc = await uow.Connection.QuerySingleOrDefaultAsync<DateTime?>(
                new CommandDefinition(
                    """
                    SELECT completed_at_utc
                    FROM operational_register_write_state
                    WHERE register_id = @R AND document_id = @D AND operation = @O;
                    """,
                    new { R = registerId, D = documentId, O = (short)OperationalRegisterWriteOperation.Post },
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));

            completedAtUtc.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Execute_WhenRegisterMissing_ThrowsClearError()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var engine = scope.ServiceProvider.GetRequiredService<IOperationalRegisterWriteEngine>();

        var act = async () => await engine.ExecuteAsync(
            registerId: Guid.CreateVersion7(),
            documentId: Guid.CreateVersion7(),
            operation: OperationalRegisterWriteOperation.Post,
            affectedPeriods: new[] { new DateOnly(2026, 1, 1) },
            writeAction: _ => Task.CompletedTask,
            manageTransaction: true,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<OperationalRegisterNotFoundException>();
    }

    private static async Task SeedRegisterAndDocumentAsync(
        IHost host,
        Guid registerId,
        Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);

        await regRepo.UpsertAsync(new OperationalRegisterUpsert(registerId, "RR", "Rent Roll"), nowUtc, CancellationToken.None);

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
