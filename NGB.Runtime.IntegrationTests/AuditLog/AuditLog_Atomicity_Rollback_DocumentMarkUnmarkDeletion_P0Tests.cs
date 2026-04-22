using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.AuditLog;
using NGB.Core.Documents;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.AuditLog;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_Atomicity_Rollback_DocumentMarkUnmarkDeletion_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string AuthSubject = "kc|audit-doc-markdel-rollback-p0";
    private static readonly DateTime NowUtc = new(2026, 2, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task MarkForDeletion_WhenAuditWriterThrowsAfterWrite_RollsBack_DocumentStatus_Audit_And_Actor()
    {
        // Arrange: audit writer throws AFTER it wrote audit rows.
        using var host = CreateHostWithThrowingAuditWriter();

        var docId = await CreateDocumentWithoutAuditAsync(
            host,
            typeCode: "general_journal_entry",
            number: "GJE-MARKDEL-RB",
            dateUtc: NowUtc,
            status: DocumentStatus.Draft,
            postedAtUtc: null,
            markedForDeletionAtUtc: null);

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            var act = () => posting.MarkForDeletionAsync(docId, CancellationToken.None);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("*simulated audit failure*");
        }

        // Assert: document is still Draft and not marked.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var doc = await repo.GetAsync(docId, CancellationToken.None);

            doc.Should().NotBeNull();
            doc!.Status.Should().Be(DocumentStatus.Draft);
            doc.MarkedForDeletionAtUtc.Should().BeNull();
        }

        // Assert: no audit rows and no actor persisted.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;"))
                .Should().Be(0, "failed MarkForDeletion must rollback audit events");

            (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;"))
                .Should().Be(0, "failed MarkForDeletion must rollback audit change rows");

            (await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
                new { s = AuthSubject }))
                .Should().Be(0, "actor upsert must rollback together with audit event");
        }
    }

    [Fact]
    public async Task UnmarkForDeletion_WhenAuditWriterThrowsAfterWrite_RollsBack_DocumentStatus_Audit_And_Actor()
    {
        // Arrange: audit writer throws AFTER it wrote audit rows.
        using var host = CreateHostWithThrowingAuditWriter();

        var docId = await CreateDocumentWithoutAuditAsync(
            host,
            typeCode: "general_journal_entry",
            number: "GJE-UNMARKDEL-RB",
            dateUtc: NowUtc,
            status: DocumentStatus.MarkedForDeletion,
            postedAtUtc: null,
            markedForDeletionAtUtc: NowUtc);

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            var act = () => posting.UnmarkForDeletionAsync(docId, CancellationToken.None);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("*simulated audit failure*");
        }

        // Assert: document is still MarkedForDeletion.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var doc = await repo.GetAsync(docId, CancellationToken.None);

            doc.Should().NotBeNull();
            doc!.Status.Should().Be(DocumentStatus.MarkedForDeletion);
            doc.MarkedForDeletionAtUtc.Should().NotBeNull();
        }

        // Assert: no audit rows and no actor persisted.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;"))
                .Should().Be(0, "failed UnmarkForDeletion must rollback audit events");

            (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;"))
                .Should().Be(0, "failed UnmarkForDeletion must rollback audit change rows");

            (await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
                new { s = AuthSubject }))
                .Should().Be(0, "actor upsert must rollback together with audit event");
        }
    }

    private IHost CreateHostWithThrowingAuditWriter()
        => IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: AuthSubject,
                        Email: "audit.doc.markdel.rollback@example.com",
                        DisplayName: "Audit Doc MarkDel Rollback")));

                services.AddScoped<PostgresAuditEventWriter>();
                services.AddScoped<IAuditEventWriter>(sp =>
                    new ThrowAfterWriteAuditEventWriter(sp.GetRequiredService<PostgresAuditEventWriter>()));
            });

    private static async Task<Guid> CreateDocumentWithoutAuditAsync(
        IHost host,
        string typeCode,
        string number,
        DateTime dateUtc,
        DocumentStatus status,
        DateTime? postedAtUtc,
        DateTime? markedForDeletionAtUtc)
    {
        var id = Guid.CreateVersion7();

        await using var scope = host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await repo.CreateAsync(new DocumentRecord
            {
                Id = id,
                TypeCode = typeCode,
                Number = number,
                DateUtc = dateUtc,
                Status = status,
                CreatedAtUtc = NowUtc,
                UpdatedAtUtc = NowUtc,
                PostedAtUtc = postedAtUtc,
                MarkedForDeletionAtUtc = markedForDeletionAtUtc
            }, ct);
        }, CancellationToken.None);

        return id;
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }

    private sealed class ThrowAfterWriteAuditEventWriter(IAuditEventWriter inner) : IAuditEventWriter
    {
        public async Task WriteAsync(AuditEvent auditEvent, CancellationToken ct = default)
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
}
