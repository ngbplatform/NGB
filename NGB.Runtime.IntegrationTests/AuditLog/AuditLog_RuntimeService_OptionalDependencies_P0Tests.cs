using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Accounting.Documents;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_RuntimeService_OptionalDependencies_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task WriteAsync_WhenAuditWriterIsNotRegistered_IsNoOp_AndDoesNotRequireTransaction()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                // Simulate AuditLog being disabled (persistence not wired).
                services.RemoveAll<IAuditEventWriter>();
                services.RemoveAll<IPlatformUserRepository>();
            });

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

            // No active transaction on purpose: must not throw when AuditLog is disabled.
            await audit.WriteAsync(
                entityKind: AuditEntityKind.Document,
                entityId: Guid.CreateVersion7(),
                actionCode: "test.audit.disabled",
                changes: null,
                metadata: new { foo = "bar" },
                correlationId: Guid.CreateVersion7(),
                ct: CancellationToken.None);
        }

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        var eventCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
        eventCount.Should().Be(0);

        var changeCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");
        changeCount.Should().Be(0);

        var userCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_users;");
        userCount.Should().Be(0);
    }

    [Fact]
    public async Task CreateDraft_WithActorButWithoutUserRepository_WritesAuditEvent_WithNullActorUserId()
    {
        const string authSubject = "kc|audit-no-user-repo";

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: authSubject,
                        Email: "no.user.repo@example.com",
                        DisplayName: "No User Repo")));

                // Intentionally remove actor repository so AuditLogService can't resolve user_id.
                services.RemoveAll<IPlatformUserRepository>();
            });

        var dateUtc = new DateTime(2026, 1, 19, 0, 0, 0, DateTimeKind.Utc);

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            documentId = await drafts.CreateDraftAsync(
                typeCode: AccountingDocumentTypeCodes.GeneralJournalEntry,
                number: "GJE-AUD-NO-USER-REPO",
                dateUtc: dateUtc,
                ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: documentId,
                    ActionCode: AuditActionCodes.DocumentCreateDraft,
                    Limit: 20,
                    Offset: 0),
                CancellationToken.None);

            events.Should().ContainSingle();
            events.Single().ActorUserId.Should().BeNull("without IPlatformUserRepository actor cannot be linked");
        }

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        var userCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
            new { s = authSubject });

        userCount.Should().Be(0, "actor upsert must not happen when IPlatformUserRepository is not registered");
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}
