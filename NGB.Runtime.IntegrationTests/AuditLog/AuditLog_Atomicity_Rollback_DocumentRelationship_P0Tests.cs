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
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

/// <summary>
/// P0: DocumentRelationshipService must be atomic with the Business AuditLog.
/// If audit writing fails after INSERT, the relationship rows + audit rows + actor upsert must rollback.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AuditLog_Atomicity_Rollback_DocumentRelationship_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string AuthSubject = "kc|audit-docrel-rollback-test";

    [Fact]
    public async Task CreateAsync_WhenAuditWriterThrowsAfterWrite_RollsBack_RelationshipRow_Audit_And_Actor()
    {
        using var host = CreateHostWithThrowingAuditWriter();

        var (fromId, toId) = await CreateTwoDraftDocsWithoutAuditAsync(host);

        // Act: create must fail (simulated audit failure).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

            var act = () => svc.CreateAsync(fromId, toId, relationshipCode: "based_on", manageTransaction: true, ct: CancellationToken.None);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("*simulated audit failure*");
        }

        // Assert: no side effects persisted.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var relCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM document_relationships WHERE from_document_id = @fromId AND to_document_id = @toId AND relationship_code_norm = 'based_on';",
                new { fromId, toId });

            relCount.Should().Be(0, "failed CreateAsync must rollback document_relationships row");

            (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;"))
                .Should().Be(0, "failed CreateAsync must rollback audit events");

            (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;"))
                .Should().Be(0, "failed CreateAsync must rollback audit change rows");

            (await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
                new { s = AuthSubject }))
                .Should().Be(0, "actor upsert must rollback together with audit event");
        }
    }

    [Fact]
    public async Task DeleteAsync_WhenAuditWriterThrowsAfterWrite_RollsBack_Delete_Audit_And_Actor()
    {
        using var host = CreateHostWithThrowingAuditWriter();

        var (fromId, toId) = await CreateTwoDraftDocsWithoutAuditAsync(host);

        var codeNorm = "based_on";
        var relId = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|{codeNorm}|{toId:D}");

        // Arrange: create the relationship row without audit (repo-level insert).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var uow = sp.GetRequiredService<IUnitOfWork>();
            var repo = sp.GetRequiredService<IDocumentRelationshipRepository>();

            await uow.ExecuteInUowTransactionAsync(async ct =>
            {
                var created = await repo.TryCreateAsync(new DocumentRelationshipRecord
                {
                    Id = relId,
                    FromDocumentId = fromId,
                    ToDocumentId = toId,
                    RelationshipCode = "based_on",
                    RelationshipCodeNorm = "based_on",
                    CreatedAtUtc = new DateTime(2026, 2, 3, 12, 0, 0, DateTimeKind.Utc)
                }, ct);

                created.Should().BeTrue();
            }, CancellationToken.None);
        }

        // Act: delete must fail (simulated audit failure).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

            var act = () => svc.DeleteAsync(fromId, toId, relationshipCode: "based_on", manageTransaction: true, ct: CancellationToken.None);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("*simulated audit failure*");
        }

        // Assert: relationship delete was rolled back and audit/actor were not committed.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var stillThere = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM document_relationships WHERE relationship_id = @id;",
                new { id = relId });

            stillThere.Should().Be(1, "failed DeleteAsync must rollback the delete");

            (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;"))
                .Should().Be(0, "failed DeleteAsync must rollback audit events");

            (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;"))
                .Should().Be(0, "failed DeleteAsync must rollback audit change rows");

            (await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
                new { s = AuthSubject }))
                .Should().Be(0, "actor upsert must rollback together with audit event");
        }
    }

    private IHost CreateHostWithThrowingAuditWriter() =>
        IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: AuthSubject,
                        Email: "audit.docrel.rollback@example.com",
                        DisplayName: "Audit DocRel Rollback")));

                // Audit writer throws AFTER it inserted audit rows (must rollback the whole transaction).
                services.AddScoped<PostgresAuditEventWriter>();
                services.AddScoped<IAuditEventWriter>(sp =>
                    new ThrowAfterWriteAuditEventWriter(sp.GetRequiredService<PostgresAuditEventWriter>()));
            });

    private static async Task<(Guid FromId, Guid ToId)> CreateTwoDraftDocsWithoutAuditAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var fromId = Guid.CreateVersion7();
        var toId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 2, 3, 10, 0, 0, DateTimeKind.Utc);

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await repo.CreateAsync(new DocumentRecord
            {
                Id = fromId,
                TypeCode = "it_alpha",
                Number = "A-0001",
                DateUtc = nowUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);

            await repo.CreateAsync(new DocumentRecord
            {
                Id = toId,
                TypeCode = "it_beta",
                Number = "B-0001",
                DateUtc = nowUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);
        }, CancellationToken.None);

        return (fromId, toId);
    }

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
    
    sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}
