using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
using NGB.Accounting.PostingState;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class CloseMonth_AlreadyClosed_WithActor_DoesNotTouchAuditOrUsers_P0Tests(PostgresTestFixture fixture)
{
    private const string Cash = "50";
    private const string Revenue = "90.1";

    private const string Subject = "kc|period-close-noop-actor-p0";
    private static readonly DateTime PeriodUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CloseMonthAsync_AlreadyClosed_WithActor_Throws_AndDoesNotWriteAuditOrTouchPlatformUsers()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(fixture.ConnectionString, services =>
        {
            services.AddScoped<ICurrentActorContext>(_ => new FixedCurrentActorContext(
                new ActorIdentity(AuthSubject: Subject, Email: "noop.period@example.com", DisplayName: "NoOp Period Actor")));
        });

        await SeedMinimalCoaAsync(host);
        await PostOnceAsync(host, Guid.CreateVersion7(), PeriodUtc);

        var period = DateOnly.FromDateTime(PeriodUtc);

        // First close (creates closed period + audit + actor).
        await CloseMonthAsync(host, period);

        // Capture baseline after successful close.
        var baseline = await CaptureBaselineAsync(fixture.ConnectionString);
        baseline.UserRow.Should().NotBeNull("successful CloseMonth should write audit with actor, which upserts platform_users");

        // Make time move forward so a stray upsert would change updated_at_utc deterministically.
        await Task.Delay(150);

        // Act
        var act = () => CloseMonthAsync(host, period);

        // Assert
        await act.Should().ThrowAsync<PeriodAlreadyClosedException>();

        var after = await CaptureBaselineAsync(fixture.ConnectionString);

        // No new audit rows must be added by an already-closed attempt.
        after.AuditEvents.Should().Be(baseline.AuditEvents);
        after.AuditChanges.Should().Be(baseline.AuditChanges);

        // No actor upsert must happen on the throw path.
        after.UsersForSubject.Should().Be(baseline.UsersForSubject);
        after.UserRow!.UpdatedAtUtc.Should().Be(baseline.UserRow!.UpdatedAtUtc);

        // And obviously, do not log additional period.close_month events.
        after.CloseMonthEventsForPeriod.Should().Be(baseline.CloseMonthEventsForPeriod);
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Cash,
            "Cash",
            AccountType.Asset,
            StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Revenue,
            "Revenue",
            AccountType.Income,
            StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }

    private static async Task PostOnceAsync(IHost host, Guid documentId, DateTime periodUtc)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, periodUtc, chart.Get(Cash), chart.Get(Revenue), amount: 100m);
            },
            ct: CancellationToken.None
        );
    }

    private static async Task CloseMonthAsync(IHost host, DateOnly period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
        await svc.CloseMonthAsync(period, closedBy: "test", CancellationToken.None);
    }

    private sealed record UserRow(Guid UserId, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

    private sealed record Baseline(
        int AuditEvents,
        int AuditChanges,
        int UsersForSubject,
        UserRow? UserRow,
        int CloseMonthEventsForPeriod);

    private static async Task<Baseline> CaptureBaselineAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var auditEvents = await conn.ExecuteScalarAsync<int>("select count(*) from platform_audit_events;");
        var auditChanges = await conn.ExecuteScalarAsync<int>("select count(*) from platform_audit_event_changes;");

        var usersForSubject = await conn.ExecuteScalarAsync<int>(
            "select count(*) from platform_users where auth_subject = @s;",
            new { s = Subject });

        var user = await conn.QuerySingleOrDefaultAsync<UserRow>(
            "select user_id as UserId, created_at_utc as CreatedAtUtc, updated_at_utc as UpdatedAtUtc from platform_users where auth_subject = @s;",
            new { s = Subject });

        var period = DateOnly.FromDateTime(PeriodUtc);

        var closeMonthEventsForPeriod = await conn.ExecuteScalarAsync<int>(
            "select count(*) from platform_audit_events where action_code = 'period.close_month' and metadata ->> 'period' = @p;",
            new { p = period.ToString("yyyy-MM-dd") });

        return new Baseline(auditEvents, auditChanges, usersForSubject, user, closeMonthEventsForPeriod);
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}
