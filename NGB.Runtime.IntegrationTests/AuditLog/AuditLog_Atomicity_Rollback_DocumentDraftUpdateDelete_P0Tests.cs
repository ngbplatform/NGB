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
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_Atomicity_Rollback_DocumentDraftUpdateDelete_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string ActorSubject = "it_draft_sub";
    private const string ActorEmail = "it_draft@ngb.local";

    private const string TypeCode = "it_doc_audit_draft_ud";
    private const string TypedTable = "doc_it_doc_audit_draft_ud";

    [Fact]
    public async Task UpdateDraftAsync_WhenAuditWriterThrowsAfterInsert_RollsBack_Document_TypedRow_Audit_And_Actor()
    {
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        // Arrange: create a draft with the default audit writer (no fault injection).
        Guid id;
        using (var hostCreate = CreateHostForDraftCreation())
        {
            var date1 = new DateTime(2026, 01, 10, 0, 0, 0, DateTimeKind.Utc);

            await using (var scope = hostCreate.Services.CreateAsyncScope())
            {
                var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
                id = await drafts.CreateDraftAsync(TypeCode, number: "N-1", dateUtc: date1, manageTransaction: true, ct: CancellationToken.None);
            }

            var baseline = await CaptureBaselineAsync(Fixture.ConnectionString);

            // Act: run UpdateDraft with a fault-injected audit writer that throws AFTER insert.
            using var hostFault = CreateHostWithFaultInjectedAuditWriter();

            var date2 = new DateTime(2026, 01, 11, 0, 0, 0, DateTimeKind.Utc);

            await using (var scope = hostFault.Services.CreateAsyncScope())
            {
                var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

                var act = async () =>
                    await drafts.UpdateDraftAsync(id, number: "N-2", dateUtc: date2, manageTransaction: true, ct: CancellationToken.None);

                await act.Should().ThrowAsync<NotSupportedException>()
                    .WithMessage("simulated audit failure");
            }

            // Assert: everything must be rolled back.
            await AssertDraftStillOriginalAsync(Fixture.ConnectionString, id, expectedNumber: "N-1", expectedDateUtc: date1);
            await AssertTypedRowUnchangedAsync(Fixture.ConnectionString, id);
            await AssertBaselineUnchangedAsync(Fixture.ConnectionString, baseline);
        }
    }

    [Fact]
    public async Task DeleteDraftAsync_WhenAuditWriterThrowsAfterInsert_RollsBack_Document_TypedRow_Audit_And_Actor()
    {
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        // Arrange: create a draft with the default audit writer (no fault injection).
        Guid id;
        using (var hostCreate = CreateHostForDraftCreation())
        {
            var date1 = new DateTime(2026, 01, 10, 0, 0, 0, DateTimeKind.Utc);

            await using (var scope = hostCreate.Services.CreateAsyncScope())
            {
                var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
                id = await drafts.CreateDraftAsync(TypeCode, number: "N-1", dateUtc: date1, manageTransaction: true, ct: CancellationToken.None);
            }

            var baseline = await CaptureBaselineAsync(Fixture.ConnectionString);

            // Act: run DeleteDraft with a fault-injected audit writer that throws AFTER insert.
            using var hostFault = CreateHostWithFaultInjectedAuditWriter();

            await using (var scope = hostFault.Services.CreateAsyncScope())
            {
                var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

                var act = async () =>
                    await drafts.DeleteDraftAsync(id, manageTransaction: true, ct: CancellationToken.None);

                await act.Should().ThrowAsync<NotSupportedException>()
                    .WithMessage("simulated audit failure");
            }

            // Assert: delete must be rolled back.
            await AssertDraftStillExistsAsync(Fixture.ConnectionString, id);
            await AssertTypedRowStillExistsAsync(Fixture.ConnectionString, id);
            await AssertBaselineUnchangedAsync(Fixture.ConnectionString, baseline);
        }
    }

    private IHost CreateHostForDraftCreation()
        => IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, ItAuditDraftDocContributor>();
                services.AddScoped<ItAuditDraftDocStorage>();

                // Ensure draft creation does not upsert the actor into platform_users.
                services.AddScoped<ICurrentActorContext>(_ => new NullCurrentActorContext());
            });

    private IHost CreateHostWithFaultInjectedAuditWriter()
        => IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, ItAuditDraftDocContributor>();

                // Replace the default audit writer with a "throw after insert" wrapper.
                services.AddScoped<PostgresAuditEventWriter>();
                services.AddScoped<IAuditEventWriter>(sp =>
                    new ThrowAfterWriteAuditEventWriter(sp.GetRequiredService<PostgresAuditEventWriter>()));

                services.AddScoped<ICurrentActorContext>(_ => new FixedCurrentActorContext(ActorSubject, ActorEmail));

                services.AddScoped<ItAuditDraftDocStorage>();
            });

    private static async Task EnsureTypedTableExistsAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var ddl = $"""
CREATE TABLE IF NOT EXISTS {TypedTable} (
    document_id UUID PRIMARY KEY REFERENCES documents(id) ON DELETE RESTRICT,
    update_calls INT NOT NULL DEFAULT 0,
    last_number TEXT NULL,
    last_date_utc TIMESTAMPTZ NULL
);
""";

        await conn.ExecuteAsync(ddl);
    }

    private static async Task<AuditBaseline> CaptureBaselineAsync(string cs)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        var auditEvents = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
        var auditChanges = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");
        var users = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_users;");
        var actorForSubject = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
            new { s = ActorSubject });

        return new AuditBaseline(auditEvents, auditChanges, users, actorForSubject);
    }

    private static async Task AssertBaselineUnchangedAsync(string cs, AuditBaseline baseline)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        var auditEvents = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
        var auditChanges = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");
        var users = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_users;");
        var actorForSubject = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
            new { s = ActorSubject });

        auditEvents.Should().Be(baseline.AuditEvents);
        auditChanges.Should().Be(baseline.AuditChanges);
        users.Should().Be(baseline.Users);
        actorForSubject.Should().Be(baseline.ActorForSubject);
    }

    private static async Task AssertDraftStillOriginalAsync(string cs, Guid id, string expectedNumber, DateTime expectedDateUtc)
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

    private static async Task AssertTypedRowUnchangedAsync(string cs, Guid id)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        var row = await conn.QuerySingleAsync<(int UpdateCalls, string? LastNumber, DateTime? LastDateUtc)>(
            $"SELECT update_calls AS UpdateCalls, last_number AS LastNumber, last_date_utc AS LastDateUtc FROM {TypedTable} WHERE document_id = @id;",
            new { id });

        row.UpdateCalls.Should().Be(0);
        row.LastNumber.Should().BeNull();
        row.LastDateUtc.Should().BeNull();
    }

    private static async Task AssertTypedRowStillExistsAsync(string cs, Guid id)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        var count = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM {TypedTable} WHERE document_id = @id;",
            new { id });

        count.Should().Be(1);
    }

    private sealed record AuditBaseline(int AuditEvents, int AuditChanges, int Users, int ActorForSubject);

    private sealed class ThrowAfterWriteAuditEventWriter(IAuditEventWriter inner) : IAuditEventWriter
    {
        public async Task WriteAsync(AuditEvent auditEvent, CancellationToken ct)
        {
            await inner.WriteAsync(auditEvent, ct);
            throw new NotSupportedException("simulated audit failure");
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
        public ActorIdentity? Current => new(subject, email, "User");
    }

    private sealed class NullCurrentActorContext : ICurrentActorContext
    {
        public ActorIdentity? Current => null;
    }

    private sealed class ItAuditDraftDocContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(TypeCode, b => b
                .Metadata(new DocumentTypeMetadata(
                    TypeCode,
                    Array.Empty<DocumentTableMetadata>(),
                    new DocumentPresentationMetadata("IT Doc Audit Draft UD"),
                    new DocumentMetadataVersion(1, "it-tests")))
                .TypedStorage<ItAuditDraftDocStorage>());
        }
    }

    private sealed class ItAuditDraftDocStorage(IUnitOfWork uow)
        : IDocumentTypeStorage, IDocumentTypeDraftFullUpdater
    {
        public string TypeCode => AuditLog_Atomicity_Rollback_DocumentDraftUpdateDelete_P0Tests.TypeCode;

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
}
