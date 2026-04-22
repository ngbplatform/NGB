using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class CloseFiscalYear_AlreadyClosed_WithActor_DoesNotTouchAuditOrUsers_P0Tests(PostgresTestFixture fixture)
{
    private const string ActorSubject = "kc|close-fy-already-closed-noop-p0";
    private static readonly DateOnly EndPeriod = new(2026, 1, 1); // month start

    [Fact]
    public async Task CloseFiscalYearAsync_CalledTwice_SecondThrows_AndDoesNotTouchAuditOrUsers()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(fixture.ConnectionString, services =>
        {
            services.AddScoped<ICurrentActorContext>(_ =>
                new FixedCurrentActorContext(new ActorIdentity(
                    AuthSubject: ActorSubject,
                    Email: "closefy.noop@example.com",
                    DisplayName: "CloseFY NoOp Actor")));
        });

        var retainedEarningsId = await SeedCoaForFiscalYearCloseAsync(host);

        // Act 1: close fiscal year successfully (no P&L activity => no closing entries, but posting_log + audit must be written)
        await CloseFiscalYearAsync(host, EndPeriod, retainedEarningsId);

        var documentId = DeterministicGuid.Create($"CloseFiscalYear|{EndPeriod:yyyy-MM-dd}");
        var baseline = await CaptureBaselineAsync(fixture.ConnectionString, documentId);

        baseline.UserUpdatedAtUtc.Should().NotBeNull("first CloseFiscalYear must upsert actor via AuditLog");

        // Act 2: close again => must fail with "already closed" and must not write audit / touch actor row
        var act = () => CloseFiscalYearAsync(host, EndPeriod, retainedEarningsId);

        await act.Should().ThrowAsync<FiscalYearAlreadyClosedException>()
            .WithMessage("*already closed*");

        var after = await CaptureBaselineAsync(fixture.ConnectionString, documentId);

        // Assert: no side effects on second (failed) attempt
        after.AuditEvents.Should().Be(baseline.AuditEvents);
        after.AuditChanges.Should().Be(baseline.AuditChanges);

        after.EventsForEntity.Should().Be(baseline.EventsForEntity);
        after.ChangesForEntity.Should().Be(baseline.ChangesForEntity);

        after.PostingLogForFiscalYearClose.Should().Be(baseline.PostingLogForFiscalYearClose);

        after.UserUpdatedAtUtc.Should().Be(baseline.UserUpdatedAtUtc,
            "failed CloseFiscalYear must not upsert/update platform_users");
    }

    private sealed record Baseline(
        int AuditEvents,
        int AuditChanges,
        int EventsForEntity,
        int ChangesForEntity,
        int PostingLogForFiscalYearClose,
        DateTime? UserUpdatedAtUtc);

    private static async Task<Baseline> CaptureBaselineAsync(string connectionString, Guid entityId)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var auditEvents = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
        var auditChanges = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");

        var eventsForEntity = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_audit_events WHERE entity_id = @id;",
            new { id = entityId });

        var changesForEntity = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_audit_event_changes c " +
            "JOIN platform_audit_events e ON e.audit_event_id = c.audit_event_id " +
            "WHERE e.entity_id = @id;",
            new { id = entityId });

        var postingLog = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting_posting_state WHERE document_id = @id AND operation = @op;",
            new { id = entityId, op = (short)PostingOperation.CloseFiscalYear });

        var userUpdatedAt = await conn.ExecuteScalarAsync<DateTime?>(
            "SELECT updated_at_utc FROM platform_users WHERE auth_subject = @s;",
            new { s = ActorSubject });

        return new Baseline(
            AuditEvents: auditEvents,
            AuditChanges: auditChanges,
            EventsForEntity: eventsForEntity,
            ChangesForEntity: changesForEntity,
            PostingLogForFiscalYearClose: postingLog,
            UserUpdatedAtUtc: userUpdatedAt);
    }

    private static async Task<Guid> SeedCoaForFiscalYearCloseAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        // Balance sheet accounts
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        // Retained Earnings (Equity, credit-normal)
        var retainedEarningsId = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "300",
            Name: "Retained earnings",
            Type: AccountType.Equity,
            StatementSection: StatementSection.Equity,
            IsContra: false,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        // P&L accounts are optional for this test (we don't post activity),
        // but we keep them to align with production setups and other tests.
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Income",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "91",
            Name: "Expenses",
            Type: AccountType.Expense,
            StatementSection: StatementSection.Expenses,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        return retainedEarningsId;
    }

    private static async Task CloseFiscalYearAsync(IHost host, DateOnly endPeriod, Guid retainedEarningsAccountId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

        await closing.CloseFiscalYearAsync(
            fiscalYearEndPeriod: endPeriod,
            retainedEarningsAccountId: retainedEarningsAccountId,
            closedBy: "test",
            ct: CancellationToken.None);
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current { get; } = actor;
    }
}
