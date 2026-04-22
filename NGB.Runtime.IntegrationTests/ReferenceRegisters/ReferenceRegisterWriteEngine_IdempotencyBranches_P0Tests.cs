using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterWriteEngine_IdempotencyBranches_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Execute_WhenAlreadyCompleted_SkipsWriteAction_AndReturnsAlreadyCompleted()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = await ArrangeSubordinateRegisterAsync(host, code: "RR_WE_ALREADY_COMPLETED");
        var documentId = await ArrangeDraftDocumentAsync(host);

        // Arrange: pre-insert a completed write state row.
        var started = DateTime.UtcNow.AddMinutes(-1);
        var completed = DateTime.UtcNow.AddSeconds(-10);
        await InsertWriteLogRowAsync(registerId, documentId, ReferenceRegisterWriteOperation.Post, started, completed);

        var called = 0;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<IReferenceRegisterWriteEngine>();

            var res = await engine.ExecuteAsync(
                registerId,
                documentId,
                ReferenceRegisterWriteOperation.Post,
                writeAction: _ =>
                {
                    Interlocked.Increment(ref called);
                    return Task.CompletedTask;
                },
                manageTransaction: true,
                ct: CancellationToken.None);

            res.Should().Be(ReferenceRegisterWriteResult.AlreadyCompleted);
        }

        called.Should().Be(0, "idempotent AlreadyCompleted must not invoke writeAction");

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var rows = await conn.QueryAsync<(DateTime StartedAtUtc, DateTime? CompletedAtUtc)>(
                """
                SELECT started_at_utc AS "StartedAtUtc", completed_at_utc AS "CompletedAtUtc"
                FROM reference_register_write_state
                WHERE register_id = @RegisterId AND document_id = @DocumentId AND operation = @Operation;
                """,
                new { RegisterId = registerId, DocumentId = documentId, Operation = (short)ReferenceRegisterWriteOperation.Post });

            rows.Should().HaveCount(1);
            rows.Single().CompletedAtUtc.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Execute_WhenInProgressFresh_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = await ArrangeSubordinateRegisterAsync(host, code: "RR_WE_IN_PROGRESS");
        var documentId = await ArrangeDraftDocumentAsync(host);

        // Arrange: pre-insert a fresh in-progress row (completed_at_utc IS NULL).
        var started = DateTime.UtcNow;
        await InsertWriteLogRowAsync(registerId, documentId, ReferenceRegisterWriteOperation.Post, started, completedAtUtc: null);

        await using var scope = host.Services.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<IReferenceRegisterWriteEngine>();

        var act = async () => await engine.ExecuteAsync(
            registerId,
            documentId,
            ReferenceRegisterWriteOperation.Post,
            writeAction: _ => Task.CompletedTask,
            manageTransaction: true,
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ReferenceRegisterWriteAlreadyInProgressException>();
        ex.Which.AssertNgbError(ReferenceRegisterWriteAlreadyInProgressException.Code, "registerId", "documentId", "operation");
    }

    [Fact]
    public async Task Execute_WhenRegisterDoesNotExist_ThrowsFailFast()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var documentId = await ArrangeDraftDocumentAsync(host);
        var missingRegisterId = Guid.CreateVersion7();

        await using var scope = host.Services.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<IReferenceRegisterWriteEngine>();

        var act = async () => await engine.ExecuteAsync(
            missingRegisterId,
            documentId,
            ReferenceRegisterWriteOperation.Post,
            writeAction: _ => Task.CompletedTask,
            manageTransaction: true,
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ReferenceRegisterNotFoundException>();
        ex.Which.AssertNgbError(ReferenceRegisterNotFoundException.Code, "registerId");
    }

    [Fact]
    public async Task Execute_WhenDocumentDoesNotExist_ThrowsFailFast()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = await ArrangeSubordinateRegisterAsync(host, code: "RR_WE_NO_DOC");
        var missingDocumentId = Guid.CreateVersion7();

        await using var scope = host.Services.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<IReferenceRegisterWriteEngine>();

        var act = async () => await engine.ExecuteAsync(
            registerId,
            missingDocumentId,
            ReferenceRegisterWriteOperation.Post,
            writeAction: _ => Task.CompletedTask,
            manageTransaction: true,
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ReferenceRegisterDocumentNotFoundException>();
        ex.Which.AssertNgbError(ReferenceRegisterDocumentNotFoundException.Code, "documentId");
    }

    private static async Task<Guid> ArrangeSubordinateRegisterAsync(IHost host, string code)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

        // Minimal metadata is sufficient for WriteEngine contract tests.
        return await mgmt.UpsertAsync(
            code,
            name: $"{code} name",
            periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
            recordMode: ReferenceRegisterRecordMode.SubordinateToRecorder,
            ct: CancellationToken.None);
    }

    private static async Task<Guid> ArrangeDraftDocumentAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        return await drafts.CreateDraftAsync(
            typeCode: "test_doc",
            number: null,
            dateUtc: DateTime.UtcNow,
            manageTransaction: true,
            ct: CancellationToken.None);
    }

    private async Task InsertWriteLogRowAsync(
        Guid registerId,
        Guid documentId,
        ReferenceRegisterWriteOperation operation,
        DateTime startedAtUtc,
        DateTime? completedAtUtc)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        await conn.ExecuteAsync(
            """
            INSERT INTO reference_register_write_state(register_id, document_id, operation, started_at_utc, completed_at_utc)
            VALUES (@RegisterId, @DocumentId, @Operation, @StartedAtUtc, @CompletedAtUtc);
            """,
            new
            {
                RegisterId = registerId,
                DocumentId = documentId,
                Operation = (short)operation,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = completedAtUtc
            });
    }
}
