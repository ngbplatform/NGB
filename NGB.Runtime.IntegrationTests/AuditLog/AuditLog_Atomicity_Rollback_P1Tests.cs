using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.PostgreSql.AuditLog;
using Npgsql;
using Xunit;
using NGB.Definitions;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_Atomicity_Rollback_P1Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    private const string TypeCode = "it_doc_audit_rollback";
    private const string Number = "D-AUD-ROLLBACK";
    private const string AuthSubject = "kc|audit-rollback-test";

    [Fact]
    public async Task CreateDraftAsync_WhenAuditWriterThrowsAfterInsert_RollsBack_Document_Audit_And_Actor()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>();
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: AuthSubject,
                        Email: "audit.rollback@example.com",
                        DisplayName: "Audit Rollback")));

                // Override the AuditEvent writer to throw AFTER it wrote the event + changes.
                // This simulates a failure happening late in the business transaction.
                services.AddScoped<PostgresAuditEventWriter>();
                services.AddScoped<IAuditEventWriter>(sp =>
                    new ThrowAfterWriteAuditEventWriter(sp.GetRequiredService<PostgresAuditEventWriter>()));
            });

        var dateUtc = new DateTime(2026, 1, 19, 12, 0, 0, DateTimeKind.Utc);

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

            var act = () => drafts.CreateDraftAsync(
                typeCode: TypeCode,
                number: Number,
                dateUtc: dateUtc,
                manageTransaction: true,
                ct: CancellationToken.None);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("*simulated audit failure*");
        }

        // Assert
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var docCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM documents WHERE type_code = @t AND number = @n;",
            new { t = TypeCode, n = Number });

        docCount.Should().Be(0, "audit failure must rollback the document draft creation");

        var eventCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_audit_events;");

        eventCount.Should().Be(0, "audit rows must not be committed if the transaction rolls back");

        var changeCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_audit_event_changes;");

        changeCount.Should().Be(0, "audit change rows must not be committed if the transaction rolls back");

        var userCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
            new { s = AuthSubject });

        userCount.Should().Be(0, "actor upsert must rollback together with audit event");
    }

    sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }

    sealed class ThrowAfterWriteAuditEventWriter(IAuditEventWriter inner) : IAuditEventWriter
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
