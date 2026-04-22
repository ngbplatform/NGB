using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_RuntimeService_Disabled_IgnoresInvalidArgs_AndDoesNotUpsert_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task WriteAsync_WhenWriterMissing_IgnoresInvalidArguments_AndDoesNotUpsertActor()
    {
        const string authSubject = "kc|disabled-but-actor-present";

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: authSubject,
                        Email: "disabled@example.com",
                        DisplayName: "Disabled User")));

                // Keep IPlatformUserRepository registered, but disable audit persistence.
                services.RemoveAll<IAuditEventWriter>();
            });

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

            // IMPORTANT: these would normally throw, but when audit is disabled it must be a strict no-op.
            await audit.WriteAsync(
                entityKind: AuditEntityKind.Document,
                entityId: Guid.Empty,
                actionCode: "   ",
                changes: [new AuditFieldChange("x", null, null)],
                metadata: new { x = 1 },
                correlationId: Guid.CreateVersion7(),
                ct: CancellationToken.None);
        }

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        var eventCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
        eventCount.Should().Be(0);

        var changeCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");
        changeCount.Should().Be(0);

        var userCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
            new { s = authSubject });

        userCount.Should().Be(0, "AuditLogService must not upsert the actor when writer is not registered");
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}
