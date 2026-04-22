using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.AuditLog;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_RuntimeService_Actor_Rollback_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string AuthSubject = "kc|p0-audit-actor-rollback";

    private static readonly ActorIdentity Actor = new(
        AuthSubject,
        Email: "p0.audit.rollback@example.com",
        DisplayName: "P0 Audit Rollback",
        IsActive: true);

    [Fact]
    public async Task WriteAsync_WithActor_WhenOuterRollback_DoesNotPersistEventOrActor()
    {
        using var host = CreateHostWithActor(Fixture.ConnectionString);

        var entityId = Guid.CreateVersion7();

        var baseline = await CaptureBaselineAsync(Fixture.ConnectionString, AuthSubject);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            await audit.WriteAsync(
                AuditEntityKind.Document,
                entityId,
                actionCode: "test.runtime.audit.rollback.actor",
                changes: null,
                metadata: new { note = "rollback" },
                correlationId: null,
                ct: CancellationToken.None);

            await uow.RollbackAsync(CancellationToken.None);
        }

        await AssertBaselineUnchangedAsync(Fixture.ConnectionString, baseline);

        // Extra safety: ensure no event is present for the entity.
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        var countForEntity = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_audit_events WHERE entity_kind = @k AND entity_id = @id;",
            new { k = (int)AuditEntityKind.Document, id = entityId });

        countForEntity.Should().Be(0);
    }

    [Fact]
    public async Task WriteAsync_WhenNoTransaction_Throws_AndDoesNotUpsertActor()
    {
        using var host = CreateHostWithActor(Fixture.ConnectionString);

        var entityId = Guid.CreateVersion7();

        var baseline = await CaptureBaselineAsync(Fixture.ConnectionString, AuthSubject);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

            Func<Task> act = () => audit.WriteAsync(
                AuditEntityKind.Document,
                entityId,
                actionCode: "test.runtime.audit.requires_tx",
                changes: null,
                metadata: null,
                correlationId: null,
                ct: CancellationToken.None);

            await act.Should().ThrowAsync<NgbInvariantViolationException>();
        }

        await AssertBaselineUnchangedAsync(Fixture.ConnectionString, baseline);
    }

    private static IHost CreateHostWithActor(string cs)
        => IntegrationHostFactory.Create(cs, services =>
        {
            services.AddScoped<ICurrentActorContext>(_ => new FixedCurrentActorContext(Actor));
        });

    private static async Task<AuditBaseline> CaptureBaselineAsync(string cs, string authSubject)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        var events = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
        var changes = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");

        var userCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
            new { s = authSubject });

        var updatedAtUtc = await conn.QuerySingleOrDefaultAsync<DateTime?>(
            "SELECT updated_at_utc FROM platform_users WHERE auth_subject = @s;",
            new { s = authSubject });

        return new AuditBaseline(events, changes, userCount, updatedAtUtc);
    }

    private static async Task AssertBaselineUnchangedAsync(string cs, AuditBaseline baseline)
    {
        var now = await CaptureBaselineAsync(cs, AuthSubject);

        now.EventCount.Should().Be(baseline.EventCount);
        now.ChangeCount.Should().Be(baseline.ChangeCount);
        now.UserCount.Should().Be(baseline.UserCount);
        now.UserUpdatedAtUtc.Should().Be(baseline.UserUpdatedAtUtc);
    }

    private sealed record AuditBaseline(
        int EventCount,
        int ChangeCount,
        int UserCount,
        DateTime? UserUpdatedAtUtc);

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current { get; } = actor;
    }
}
