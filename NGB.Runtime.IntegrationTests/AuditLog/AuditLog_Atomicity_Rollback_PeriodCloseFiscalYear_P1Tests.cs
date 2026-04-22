using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.Dimensions;
using NGB.Core.AuditLog;
using NGB.Persistence.Accounts;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.AuditLog;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_Atomicity_Rollback_PeriodCloseFiscalYear_P1Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    private const string AuthSubject = "kc|fy-close-audit-rollback";

    [Fact]
    public async Task CloseFiscalYear_WhenAuditWriterThrowsAfterInsert_RollsBack_PostingLog_Audit_And_Actor()
    {
        // Arrange
        var fiscalYearEndPeriod = new DateOnly(2026, 1, 1);
        var expectedDocumentId = DeterministicGuid.Create($"CloseFiscalYear|{fiscalYearEndPeriod:yyyy-MM-dd}");

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: AuthSubject,
                        Email: "fy.audit.rollback@example.com",
                        DisplayName: "FY Close Audit Rollback")));

                // Override the AuditEvent writer to throw AFTER it wrote the event + changes.
                // IMPORTANT: only throw for CloseFiscalYear so that unrelated setup can still succeed.
                services.AddScoped<PostgresAuditEventWriter>();
                services.AddScoped<IAuditEventWriter>(sp =>
                    new ThrowAfterWriteAuditEventWriter(
                        sp.GetRequiredService<PostgresAuditEventWriter>(),
                        actionCodeToFail: AuditActionCodes.PeriodCloseFiscalYear));
            });

        // Seed retained earnings WITHOUT AuditLog (to keep audit tables empty prior to the failing operation).
        var retainedEarningsId = Guid.CreateVersion7();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();

            var acc = new Account(
                id: retainedEarningsId,
                code: "300",
                name: "Retained Earnings",
                type: AccountType.Equity,
                statementSection: StatementSection.Equity,
                negativeBalancePolicy: NegativeBalancePolicy.Allow,
                isContra: false,
                dimensionRules: Array.Empty<AccountDimensionRule>());

            await uow.ExecuteInUowTransactionAsync(async ct =>
            {
                await repo.CreateAsync(acc, isActive: true, ct);
            }, CancellationToken.None);
        }

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

            var act = () => svc.CloseFiscalYearAsync(
                fiscalYearEndPeriod,
                retainedEarningsAccountId: retainedEarningsId,
                closedBy: "fy-closer",
                ct: CancellationToken.None);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("*simulated audit failure*");
        }

        // Assert
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var postingLogCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting_posting_state WHERE document_id = @id AND operation = 4;",
            new { id = expectedDocumentId });

        postingLogCount.Should().Be(0, "audit failure must rollback CloseFiscalYear idempotency state (posting_log)");

        var regCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting_register_main WHERE document_id = @id;",
            new { id = expectedDocumentId });

        regCount.Should().Be(0, "no accounting register side-effects may be committed when the transaction rolls back");

        var turnoverCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting_turnovers WHERE period = @p AND account_id = @a;",
            new { p = fiscalYearEndPeriod, a = retainedEarningsId });

        turnoverCount.Should().Be(0, "no turnovers may be committed when the transaction rolls back");

        var eventCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_audit_events;");

        eventCount.Should().Be(0, "audit rows must not be committed if CloseFiscalYear transaction rolls back");

        var changeCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_audit_event_changes;");

        changeCount.Should().Be(0, "audit change rows must not be committed if CloseFiscalYear transaction rolls back");

        var userCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
            new { s = AuthSubject });

        userCount.Should().Be(0, "actor upsert must rollback together with audit event");
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }

    private sealed class ThrowAfterWriteAuditEventWriter(IAuditEventWriter inner, string actionCodeToFail) : IAuditEventWriter
    {
        public async Task WriteAsync(AuditEvent auditEvent, CancellationToken ct = default)
        {
            await inner.WriteAsync(auditEvent, ct);

            if (string.Equals(auditEvent.ActionCode, actionCodeToFail, StringComparison.Ordinal))
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
