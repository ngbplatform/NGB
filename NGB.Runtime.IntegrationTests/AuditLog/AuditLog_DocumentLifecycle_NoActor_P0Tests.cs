using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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
public sealed class AuditLog_DocumentLifecycle_NoActor_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CreateDraft_WhenActorIsNull_WritesAuditEvent_WithNullActorUserId_AndDoesNotInsertUsers()
    {
        // Default DI registers NullCurrentActorContext, so actor is absent.
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var dateUtc = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc);

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            documentId = await drafts.CreateDraftAsync(
                typeCode: AccountingDocumentTypeCodes.GeneralJournalEntry,
                dateUtc: dateUtc,
                number: "GJE-NOACTOR-0001",
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
                    Limit: 10,
                    Offset: 0),
                CancellationToken.None);

            events.Should().ContainSingle();
            events.Single().ActorUserId.Should().BeNull();
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var userCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_users;");
            userCount.Should().Be(0, "no actor => audit log must not upsert platform users");
        }
    }
}
