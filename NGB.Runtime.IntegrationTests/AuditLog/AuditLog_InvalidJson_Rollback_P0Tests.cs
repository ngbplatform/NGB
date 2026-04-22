using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_InvalidJson_Rollback_P0Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task WriteAuditEvent_WhenMetadataJsonIsInvalid_Throws_AndDoesNotPersistAnything()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var entityId = Guid.CreateVersion7();
        var occurredAtUtc = new DateTime(2026, 1, 19, 10, 0, 0, DateTimeKind.Utc);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditEventWriter>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            var ev = new AuditEvent(
                AuditEventId: Guid.CreateVersion7(),
                EntityKind: AuditEntityKind.Document,
                EntityId: entityId,
                ActionCode: "test.invalid_json.metadata",
                ActorUserId: null,
                OccurredAtUtc: occurredAtUtc,
                CorrelationId: null,
                MetadataJson: "{ not valid json",
                Changes: Array.Empty<AuditFieldChange>());

            Func<Task> act = () => writer.WriteAsync(ev, CancellationToken.None);

            await act.Should().ThrowAsync<PostgresException>()
                .WithMessage("*invalid input syntax for type json*");

            await uow.RollbackAsync(CancellationToken.None);
        }

        (await CountAuditEventsAsync(Fixture.ConnectionString)).Should().Be(0);
        (await CountAuditChangesAsync(Fixture.ConnectionString)).Should().Be(0);
    }

    [Fact]
    public async Task WriteAuditEvent_WhenChangeJsonIsInvalid_Throws_AndRollbackRemovesEvent()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var entityId = Guid.CreateVersion7();
        var occurredAtUtc = new DateTime(2026, 1, 19, 11, 0, 0, DateTimeKind.Utc);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditEventWriter>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            var ev = new AuditEvent(
                AuditEventId: Guid.CreateVersion7(),
                EntityKind: AuditEntityKind.Document,
                EntityId: entityId,
                ActionCode: "test.invalid_json.change",
                ActorUserId: null,
                OccurredAtUtc: occurredAtUtc,
                CorrelationId: null,
                MetadataJson: null,
                Changes: new[]
                {
                    new AuditFieldChange(
                        FieldPath: "some.field",
                        OldValueJson: "{ not valid json",
                        NewValueJson: "null")
                });

            Func<Task> act = () => writer.WriteAsync(ev, CancellationToken.None);

            await act.Should().ThrowAsync<PostgresException>()
                .WithMessage("*invalid input syntax for type json*");

            // Important: the event row was inserted before changes batch insert failed.
            // Rollback must remove both the event and its changes.
            await uow.RollbackAsync(CancellationToken.None);
        }

        (await CountAuditEventsAsync(Fixture.ConnectionString)).Should().Be(0);
        (await CountAuditChangesAsync(Fixture.ConnectionString)).Should().Be(0);
    }

    private static async Task<int> CountAuditEventsAsync(string cs)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
    }

    private static async Task<int> CountAuditChangesAsync(string cs)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");
    }
}
