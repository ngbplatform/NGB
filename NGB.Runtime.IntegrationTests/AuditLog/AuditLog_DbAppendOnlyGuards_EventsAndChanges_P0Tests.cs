using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_DbAppendOnlyGuards_EventsAndChanges_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime NowUtc = new(2026, 2, 4, 12, 0, 0, DateTimeKind.Utc);
    private const string KnownTypeCode = "general_journal_entry";
    private sealed record AuditChangeRow(Guid AuditEventId, int Ordinal);

    [Fact]
    public async Task AuditEvents_Table_IsAppendOnly_UpdateAndDeleteAreForbidden()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid docId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            docId = await drafts.CreateDraftAsync(KnownTypeCode, "AUD-APP-1", NowUtc, manageTransaction: true, ct: CancellationToken.None);
        }

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var eventId = await conn.ExecuteScalarAsync<Guid?>(
            "SELECT audit_event_id FROM platform_audit_events WHERE entity_id = @id ORDER BY occurred_at_utc DESC, audit_event_id DESC LIMIT 1;",
            new { id = docId });

        eventId.Should().NotBeNull("creating a draft must produce an audit event");

        // UPDATE should be blocked.
        var updateAct = async () =>
        {
            await conn.ExecuteAsync(
                "UPDATE platform_audit_events SET occurred_at_utc = occurred_at_utc WHERE audit_event_id = @id;",
                new { id = eventId!.Value });
        };

        var updateEx = await updateAct.Should().ThrowAsync<PostgresException>();
        updateEx.Which.SqlState.Should().BeOneOf("55000", "P0001");

        // DELETE should be blocked.
        var deleteAct = async () =>
        {
            await conn.ExecuteAsync(
                "DELETE FROM platform_audit_events WHERE audit_event_id = @id;",
                new { id = eventId!.Value });
        };

        var deleteEx = await deleteAct.Should().ThrowAsync<PostgresException>();
        deleteEx.Which.SqlState.Should().BeOneOf("55000", "P0001");
    }

    [Fact]
    public async Task AuditEventChanges_Table_IsAppendOnly_UpdateAndDeleteAreForbidden()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // Ensure we have at least one event with changes.
        Guid docId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            docId = await drafts.CreateDraftAsync(KnownTypeCode, "AUD-APP-2", NowUtc, manageTransaction: true, ct: CancellationToken.None);

            // Make a change to reliably produce "changes" rows.
            await drafts.UpdateDraftAsync(docId, number: "AUD-APP-2B", dateUtc: null, manageTransaction: true, ct: CancellationToken.None);
        }

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var one = await conn.QuerySingleOrDefaultAsync<AuditChangeRow>(
            "SELECT audit_event_id AS AuditEventId, ordinal AS Ordinal " +
            "FROM platform_audit_event_changes " +
            "ORDER BY audit_event_id DESC, ordinal DESC LIMIT 1;");

        one.Should().NotBeNull("updating draft must produce audit changes");
        var latestChange = one!;
        latestChange.AuditEventId.Should().NotBe(Guid.Empty, "updating draft must produce audit changes");

        // UPDATE should be blocked.
        var updateAct = async () =>
        {
            await conn.ExecuteAsync(
                "UPDATE platform_audit_event_changes SET new_value_jsonb = new_value_jsonb " +
                "WHERE audit_event_id = @id AND ordinal = @ord;",
                new { id = latestChange.AuditEventId, ord = latestChange.Ordinal });
        };

        var updateEx = await updateAct.Should().ThrowAsync<PostgresException>();
        updateEx.Which.SqlState.Should().BeOneOf("55000", "P0001");

        // DELETE should be blocked.
        var deleteAct = async () =>
        {
            await conn.ExecuteAsync(
                "DELETE FROM platform_audit_event_changes WHERE audit_event_id = @id AND ordinal = @ord;",
                new { id = latestChange.AuditEventId, ord = latestChange.Ordinal });
        };

        var deleteEx = await deleteAct.Should().ThrowAsync<PostgresException>();
        deleteEx.Which.SqlState.Should().BeOneOf("55000", "P0001");
    }
}
