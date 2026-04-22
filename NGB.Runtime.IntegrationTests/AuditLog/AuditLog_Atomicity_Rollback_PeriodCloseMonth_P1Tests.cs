using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.PostgreSql.AuditLog;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_Atomicity_Rollback_PeriodCloseMonth_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string AuthSubject = "kc|audit-period-closemonth-rollback-test";
    private static readonly AuditActorGate Gate = new();

    [Fact]
    public async Task CloseMonth_WhenAuditWriterThrowsAfterInsert_RollsBack_Balances_ClosedPeriod_Audit_And_Actor()
    {
        var period = new DateOnly(2026, 3, 1);
        var day1Utc = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodDate = period.ToDateTime(TimeOnly.MinValue);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                // Important: we only want the seeded objects (CoA, posting, etc.) to be created
                // without an actor, so that we can assert that the actor-upsert is rolled back
                // together with the CloseMonth transaction.
                var actor = new ActorIdentity(
                    AuthSubject: AuthSubject,
                    Email: "audit.period.closemonth.rollback@example.com",
                    DisplayName: "Audit Period CloseMonth Rollback");

                services.AddScoped<ICurrentActorContext>(_ => new ConditionalCurrentActorContext(actor, Gate));

                // Override the AuditEvent writer to throw AFTER it wrote the event + changes.
                // This simulates a failure happening late in the business transaction.
                services.AddScoped<PostgresAuditEventWriter>();
                services.AddScoped<IAuditEventWriter>(sp =>
                    new ThrowAfterWriteAuditEventWriter(
                        sp.GetRequiredService<PostgresAuditEventWriter>(),
                        AuditActionCodes.PeriodCloseMonth));
            });

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await ReportingTestHelpers.PostAsync(
            host,
            Guid.CreateVersion7(),
            day1Utc,
            debitCode: "50",
            creditCode: "90.1",
            amount: 10m);

        // Baseline state after seed/post. We only verify that the CloseMonth attempt does not change it.
        await using var baselineConn = new NpgsqlConnection(Fixture.ConnectionString);
        await baselineConn.OpenAsync();

        var baselineEventCount = await baselineConn.ExecuteScalarAsync<int>("select count(*) from platform_audit_events;");
        var baselineChangeCount = await baselineConn.ExecuteScalarAsync<int>("select count(*) from platform_audit_event_changes;");
        var baselineCloseMonthEventCount = await baselineConn.ExecuteScalarAsync<int>(
            "select count(*) from platform_audit_events where action_code = @a;",
            new { a = AuditActionCodes.PeriodCloseMonth });

        var baselineUserCount = await baselineConn.ExecuteScalarAsync<int>(
            "select count(*) from platform_users where auth_subject = @s;",
            new { s = AuthSubject });

        // Act
        Task Act()
        {
            using var _ = Gate.Enable();
            return ReportingTestHelpers.CloseMonthAsync(host, period, closedBy: "closer");
        }

        var act = () => Act();
        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*simulated audit failure*");

        // Assert: nothing from CloseMonth is persisted.
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var balances = await conn.ExecuteScalarAsync<int>(
            "select count(*) from accounting_balances where period = @p;",
            new { p = periodDate });

        balances.Should().Be(0, "audit failure must rollback computed balances for the month close");

        var closed = await conn.ExecuteScalarAsync<int>(
            "select count(*) from accounting_closed_periods where period = @p;",
            new { p = periodDate });

        closed.Should().Be(0, "audit failure must rollback the closed-period marker");

        var eventCount = await conn.ExecuteScalarAsync<int>("select count(*) from platform_audit_events;");
        eventCount.Should().Be(baselineEventCount, "audit rows for CloseMonth must not be committed if the transaction rolls back");

        var changeCount = await conn.ExecuteScalarAsync<int>("select count(*) from platform_audit_event_changes;");
        changeCount.Should().Be(baselineChangeCount, "audit change rows for CloseMonth must not be committed if the transaction rolls back");

        var closeMonthEventCount = await conn.ExecuteScalarAsync<int>(
            "select count(*) from platform_audit_events where action_code = @a;",
            new { a = AuditActionCodes.PeriodCloseMonth });

        closeMonthEventCount.Should().Be(baselineCloseMonthEventCount, "the CloseMonth audit event must rollback");

        var userCount = await conn.ExecuteScalarAsync<int>(
            "select count(*) from platform_users where auth_subject = @s;",
            new { s = AuthSubject });

        userCount.Should().Be(baselineUserCount, "actor upsert must rollback together with the CloseMonth audit event");
    }

    sealed class ConditionalCurrentActorContext(ActorIdentity actor, AuditActorGate gate) : ICurrentActorContext
    {
        public ActorIdentity? Current => gate.IsEnabled ? actor : null;
    }

    sealed class AuditActorGate
    {
        private readonly AsyncLocal<bool> _enabled = new();

        public bool IsEnabled => _enabled.Value;

        public IDisposable Enable()
        {
            var prev = _enabled.Value;
            _enabled.Value = true;
            return new Revert(() => _enabled.Value = prev);
        }

        sealed class Revert(Action dispose) : IDisposable
        {
            public void Dispose() => dispose();
        }
    }

    sealed class ThrowAfterWriteAuditEventWriter(IAuditEventWriter inner, string actionCodeToThrow) : IAuditEventWriter
    {
        public async Task WriteAsync(AuditEvent auditEvent, CancellationToken ct = default)
        {
            await inner.WriteAsync(auditEvent, ct);

            // Important: we only simulate the failure for the action under test.
            // Test fixtures (e.g. reporting seed) may write other audit events.
            if (auditEvent.ActionCode == actionCodeToThrow)
            {
                throw new NotSupportedException("simulated audit failure");
            }
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
