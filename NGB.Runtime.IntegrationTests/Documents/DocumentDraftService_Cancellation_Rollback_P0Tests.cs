using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.AuditLog;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Documents.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.AuditLog;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentDraftService_Cancellation_Rollback_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string ActorSubject = "it_cancel_draft_sub";
    private const string ActorEmail = "it_cancel_draft@ngb.local";

    // IMPORTANT:
    // Use unique typeCode/table names to avoid colliding with real module typed tables.
    private const string TypeCode = "it_doc_cancel_draft";
    private const string TypedTable = "doc_it_doc_cancel_draft";

    [Fact]
    public async Task CreateDraftAsync_WhenCancellationDuringAuditWrite_RollsBack_Document_Typed_Audit_And_Actor()
    {
        await Fixture.ResetDatabaseAsync();
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHostWithCancelableAuditWriter();

        var baseline = await CaptureBaselineAsync(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
        var writer = scope.ServiceProvider.GetRequiredService<CancelAfterAuditWriteEventWriter>();

        using var cts = new CancellationTokenSource();

        var dateUtc = new DateTime(2026, 02, 01, 0, 0, 0, DateTimeKind.Utc);
        var task = drafts.CreateDraftAsync(TypeCode, number: "N-1", dateUtc: dateUtc, manageTransaction: true, ct: cts.Token);

        var ev = await writer.AfterFirstWrite.WaitAsync(TimeSpan.FromSeconds(10));
        ev.ActionCode.Should().Be(AuditActionCodes.DocumentCreateDraft);
        ev.EntityKind.Should().Be(AuditEntityKind.Document);
        ev.EntityId.Should().NotBe(Guid.Empty);
        ev.ActorUserId.Should().NotBeNull();

        // Cancel *after* the audit writer has inserted the event (in the same transaction).
        cts.Cancel();

        Func<Task> act = async () => await task;
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Assert: all side effects must be rolled back.
        await AssertDocumentDoesNotExistAsync(Fixture.ConnectionString, ev.EntityId);
        await AssertTypedRowDoesNotExistAsync(Fixture.ConnectionString, ev.EntityId);
        await AssertNoAuditForDocumentAsync(Fixture.ConnectionString, ev.EntityId);
        await AssertBaselineUnchangedAsync(Fixture.ConnectionString, baseline);
    }

    [Fact]
    public async Task UpdateDraftAsync_WhenCancellationDuringAuditWrite_RollsBack_Document_Typed_Audit_And_Actor()
    {
        await Fixture.ResetDatabaseAsync();
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        Guid id;
        var date1 = new DateTime(2026, 02, 01, 0, 0, 0, DateTimeKind.Utc);

        using (var hostCreate = CreateHostForDraftSetup())
        {
            await using var scope = hostCreate.Services.CreateAsyncScope();
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            id = await drafts.CreateDraftAsync(TypeCode, number: "N-1", dateUtc: date1, manageTransaction: true, ct: CancellationToken.None);
        }

        await AssertDraftHeaderAsync(Fixture.ConnectionString, id, expectedNumber: "N-1", expectedDateUtc: date1);
        await AssertTypedRowAsync(Fixture.ConnectionString, id, expectedUpdateCalls: 0, expectedLastNumber: null, expectedLastDateUtc: null);

        var baseline = await CaptureBaselineAsync(Fixture.ConnectionString);

        using var host = CreateHostWithCancelableAuditWriter();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            var writer = scope.ServiceProvider.GetRequiredService<CancelAfterAuditWriteEventWriter>();
            using var cts = new CancellationTokenSource();

            var date2 = new DateTime(2026, 02, 02, 0, 0, 0, DateTimeKind.Utc);
            var task = drafts.UpdateDraftAsync(id, number: "N-2", dateUtc: date2, manageTransaction: true, ct: cts.Token);

            var ev = await writer.AfterFirstWrite.WaitAsync(TimeSpan.FromSeconds(10));
            ev.ActionCode.Should().Be(AuditActionCodes.DocumentUpdateDraft);
            ev.EntityId.Should().Be(id);
            ev.ActorUserId.Should().NotBeNull();

            cts.Cancel();

            Func<Task> act = async () => await task;
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        // Assert: update must be rolled back (documents + typed hook + audit + actor).
        await AssertDraftHeaderAsync(Fixture.ConnectionString, id, expectedNumber: "N-1", expectedDateUtc: date1);
        await AssertTypedRowAsync(Fixture.ConnectionString, id, expectedUpdateCalls: 0, expectedLastNumber: null, expectedLastDateUtc: null);
        await AssertBaselineUnchangedAsync(Fixture.ConnectionString, baseline);
    }

    [Fact]
    public async Task DeleteDraftAsync_WhenCancellationDuringAuditWrite_RollsBack_Document_Typed_Audit_And_Actor()
    {
        await Fixture.ResetDatabaseAsync();
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        Guid id;
        var date1 = new DateTime(2026, 02, 01, 0, 0, 0, DateTimeKind.Utc);

        using (var hostCreate = CreateHostForDraftSetup())
        {
            await using var scope = hostCreate.Services.CreateAsyncScope();
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            id = await drafts.CreateDraftAsync(TypeCode, number: "N-1", dateUtc: date1, manageTransaction: true, ct: CancellationToken.None);
        }

        await AssertDraftStillExistsAsync(Fixture.ConnectionString, id);
        await AssertTypedRowStillExistsAsync(Fixture.ConnectionString, id);

        var baseline = await CaptureBaselineAsync(Fixture.ConnectionString);

        using var host = CreateHostWithCancelableAuditWriter();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            var writer = scope.ServiceProvider.GetRequiredService<CancelAfterAuditWriteEventWriter>();
            using var cts = new CancellationTokenSource();

            var task = drafts.DeleteDraftAsync(id, manageTransaction: true, ct: cts.Token);

            var ev = await writer.AfterFirstWrite.WaitAsync(TimeSpan.FromSeconds(10));
            ev.ActionCode.Should().Be(AuditActionCodes.DocumentDeleteDraft);
            ev.EntityId.Should().Be(id);
            ev.ActorUserId.Should().NotBeNull();

            cts.Cancel();

            Func<Task> act = async () => await task;
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        // Assert: delete must be rolled back (typed delete + registry delete + audit + actor).
        await AssertDraftStillExistsAsync(Fixture.ConnectionString, id);
        await AssertTypedRowStillExistsAsync(Fixture.ConnectionString, id);
        await AssertBaselineUnchangedAsync(Fixture.ConnectionString, baseline);
    }

    private IHost CreateHostForDraftSetup()
        => IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, ItDocCancelDraftContributor>();
                services.AddScoped<ItDocCancelDraftStorage>();

                // Setup host must not insert users.
                services.AddScoped<ICurrentActorContext>(_ => new NullCurrentActorContext());
            });

    private IHost CreateHostWithCancelableAuditWriter()
        => IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, ItDocCancelDraftContributor>();
                services.AddScoped<ItDocCancelDraftStorage>();

                // Replace the default audit writer with a cancellation-style wrapper
                // that blocks after insert and only fails once the token is canceled.
                services.AddScoped<PostgresAuditEventWriter>();
                services.AddScoped<CancelAfterAuditWriteEventWriter>();
                services.AddScoped<IAuditEventWriter>(sp => sp.GetRequiredService<CancelAfterAuditWriteEventWriter>());

                services.AddScoped<ICurrentActorContext>(_ => new FixedCurrentActorContext(ActorSubject, ActorEmail));
            });

    private static async Task EnsureTypedTableExistsAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        // FK RESTRICT/NO ACTION to ensure rollback restores typed rows for delete scenarios.
        var ddl = $"""
CREATE TABLE IF NOT EXISTS {TypedTable} (
    document_id UUID PRIMARY KEY REFERENCES documents(id) ON DELETE RESTRICT,
    update_calls INT NOT NULL DEFAULT 0,
    last_number TEXT NULL,
    last_date_utc TIMESTAMPTZ NULL,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
""";

        await conn.ExecuteAsync(ddl);
    }

    private sealed record Baseline(int AuditEvents, int AuditChanges, int Users, int ActorForSubject);

    private static async Task<Baseline> CaptureBaselineAsync(string cs)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        var auditEvents = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
        var auditChanges = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");
        var users = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_users;");
        var actorForSubject = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
            new { s = ActorSubject });

        return new Baseline(auditEvents, auditChanges, users, actorForSubject);
    }

    private static async Task AssertBaselineUnchangedAsync(string cs, Baseline baseline)
    {
        var current = await CaptureBaselineAsync(cs);
        current.AuditEvents.Should().Be(baseline.AuditEvents);
        current.AuditChanges.Should().Be(baseline.AuditChanges);
        current.Users.Should().Be(baseline.Users);
        current.ActorForSubject.Should().Be(baseline.ActorForSubject);
    }

    private static async Task AssertNoAuditForDocumentAsync(string cs, Guid documentId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        // platform_audit_events is append-only; if rollback worked there must be no rows for that entity.
        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_audit_events WHERE entity_kind = @k AND entity_id = @id;",
            new { k = (short)AuditEntityKind.Document, id = documentId });

        count.Should().Be(0);
    }

    private static async Task AssertDocumentDoesNotExistAsync(string cs, Guid documentId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        var docCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM documents WHERE id = @id;",
            new { id = documentId });

        docCount.Should().Be(0);
    }

    private static async Task AssertTypedRowDoesNotExistAsync(string cs, Guid documentId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        var typedCount = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM {TypedTable} WHERE document_id = @id;",
            new { id = documentId });

        typedCount.Should().Be(0);
    }

    private static async Task AssertDraftHeaderAsync(string cs, Guid id, string? expectedNumber, DateTime expectedDateUtc)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        var row = await conn.QuerySingleAsync<(string? Number, DateTime DateUtc, short Status)>(
            "SELECT number AS Number, date_utc AS DateUtc, status AS Status FROM documents WHERE id = @id;",
            new { id });

        row.Number.Should().Be(expectedNumber);
        row.DateUtc.Should().Be(expectedDateUtc);
        ((DocumentStatus)row.Status).Should().Be(DocumentStatus.Draft);
    }

    private static async Task AssertDraftStillExistsAsync(string cs, Guid id)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        var status = await conn.ExecuteScalarAsync<short>(
            "SELECT status FROM documents WHERE id = @id;",
            new { id });

        ((DocumentStatus)status).Should().Be(DocumentStatus.Draft);
    }

    private static async Task AssertTypedRowStillExistsAsync(string cs, Guid id)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        var count = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {TypedTable} WHERE document_id = @id;", new { id });
        count.Should().Be(1);
    }

    private static async Task AssertTypedRowAsync(string cs, Guid id, int expectedUpdateCalls, string? expectedLastNumber, DateTime? expectedLastDateUtc)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        var row = await conn.QuerySingleAsync<(int UpdateCalls, string? LastNumber, DateTime? LastDateUtc)>(
            $"SELECT update_calls AS UpdateCalls, last_number AS LastNumber, last_date_utc AS LastDateUtc FROM {TypedTable} WHERE document_id = @id;",
            new { id });

        row.UpdateCalls.Should().Be(expectedUpdateCalls);
        row.LastNumber.Should().Be(expectedLastNumber);
        row.LastDateUtc.Should().Be(expectedLastDateUtc);
    }

    private sealed class ItDocCancelDraftContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(TypeCode, b => b
                .Metadata(new DocumentTypeMetadata(
                    TypeCode,
                    Array.Empty<DocumentTableMetadata>(),
                    new DocumentPresentationMetadata("IT Doc Cancel Draft"),
                    new DocumentMetadataVersion(1, "it-tests")))
                .TypedStorage<ItDocCancelDraftStorage>());
        }
    }

    private sealed class ItDocCancelDraftStorage(IUnitOfWork uow)
        : IDocumentTypeStorage, IDocumentTypeDraftFullUpdater
    {
        public string TypeCode => DocumentDraftService_Cancellation_Rollback_P0Tests.TypeCode;

        public async Task CreateDraftAsync(Guid documentId, CancellationToken ct = default)
        {
            uow.EnsureActiveTransaction();

            var sql = $"INSERT INTO {TypedTable} (document_id) VALUES (@documentId) ON CONFLICT (document_id) DO NOTHING;";
            await uow.Connection.ExecuteAsync(new CommandDefinition(sql, new { documentId }, uow.Transaction, cancellationToken: ct));
        }

        public async Task DeleteDraftAsync(Guid documentId, CancellationToken ct = default)
        {
            uow.EnsureActiveTransaction();

            var sql = $"DELETE FROM {TypedTable} WHERE document_id = @documentId;";
            await uow.Connection.ExecuteAsync(new CommandDefinition(sql, new { documentId }, uow.Transaction, cancellationToken: ct));
        }

        public async Task UpdateDraftAsync(DocumentRecord updatedDraft, CancellationToken ct = default)
        {
            uow.EnsureActiveTransaction();

            var sql = $"""
UPDATE {TypedTable}
SET update_calls = update_calls + 1,
    last_number = @number,
    last_date_utc = @dateUtc
WHERE document_id = @documentId;
""";

            await uow.Connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new
                    {
                        documentId = updatedDraft.Id,
                        number = updatedDraft.Number,
                        dateUtc = updatedDraft.DateUtc
                    },
                    uow.Transaction,
                    cancellationToken: ct));
        }
    }

    /// <summary>
    /// Writes the audit event (so we know it was persisted in the current transaction) and then blocks
    /// until the provided token is canceled, at which point it throws <see cref="OperationCanceledException" />.
    ///
    /// This simulates a cancellation happening after business changes and audit writes,
    /// so rollback must remove:
    /// - business rows (documents + typed table)
    /// - audit rows (events + changes)
    /// - actor upsert (platform_users)
    /// </summary>
    private sealed class CancelAfterAuditWriteEventWriter(PostgresAuditEventWriter inner) : IAuditEventWriter
    {
        private readonly TaskCompletionSource<AuditEvent> _afterFirstWrite =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AuditEvent> AfterFirstWrite => _afterFirstWrite.Task;

        public async Task WriteAsync(AuditEvent auditEvent, CancellationToken ct)
        {
            await inner.WriteAsync(auditEvent, ct);
            _afterFirstWrite.TrySetResult(auditEvent);

            // Do not return: keep the transaction open and fail only after cancellation.
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }

        public async Task WriteBatchAsync(IReadOnlyList<AuditEvent> auditEvents, CancellationToken ct = default)
        {
            if (auditEvents is null)
                throw new ArgumentNullException(nameof(auditEvents));

            for (var i = 0; i < auditEvents.Count; i++)
                await WriteAsync(auditEvents[i], ct);
        }
    }

    private sealed class FixedCurrentActorContext(string subject, string? email) : ICurrentActorContext
    {
        public ActorIdentity? Current => new ActorIdentity(
            AuthSubject: subject,
            Email: email,
            DisplayName: "IT Cancel Draft",
            IsActive: true);
    }
}
