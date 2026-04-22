using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
using NGB.Accounting.PostingState;
using NGB.Persistence.Accounts;
using NGB.Runtime.Accounts;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class CloseFiscalYear_EndPeriodClosed_WithActor_DoesNotTouchAuditOrUsers_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string AuthSubject = "kc|fy-close-endperiod-closed-noop-actor";
    private static readonly DateOnly FiscalYearEndPeriod = new(2025, 12, 1);
    private sealed record PlatformUserRow(Guid UserId, DateTime UpdatedAtUtc);

    [Fact]
    public async Task CloseFiscalYearAsync_WhenEndPeriodIsClosed_WithActor_ShouldThrow_AndNotTouchAuditOrUsers()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: AuthSubject,
                        Email: "fy.endperiod.closed@example.com",
                        DisplayName: "FY Close EndPeriod Closed")));
            });

        var ids = await SeedCoAAsync(host);

        // Create some P&L activity so FY close would otherwise do work.
        var period1 = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var period2 = new DateTime(2025, 6, 16, 12, 0, 0, DateTimeKind.Utc);
        await PostAsync(host, Guid.CreateVersion7(), period1, debitCode: "50", creditCode: "90.1", amount: 100m); // Dr Cash / Cr Revenue
        await PostAsync(host, Guid.CreateVersion7(), period2, debitCode: "91", creditCode: "50", amount: 40m);    // Dr Expense / Cr Cash

        // Close all months including end-period month. FY close must post into an OPEN end-period.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var periodClosing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            for (var m = 1; m <= 12; m++)
                await periodClosing.CloseMonthAsync(new DateOnly(2025, m, 1), closedBy: "test", CancellationToken.None);
        }

        var baseline = await CaptureBaselineAsync();

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var periodClosing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

            var act = () => periodClosing.CloseFiscalYearAsync(
                FiscalYearEndPeriod,
                retainedEarningsAccountId: ids.RetainedEarningsId,
                closedBy: "test",
                CancellationToken.None);

            var ex = await act.Should().ThrowAsync<PeriodAlreadyClosedException>();
            ex.Which.Message.Should().Contain("closed");
            ex.Which.Message.Should().Contain(FiscalYearEndPeriod.ToString("yyyy-MM-dd"));
        }

        // Assert: the failing call must not touch audit or actor.
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;"))
            .Should().Be(baseline.AuditEvents);

        (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;"))
            .Should().Be(baseline.AuditChanges);

        var userRow = await conn.QuerySingleOrDefaultAsync<PlatformUserRow>(
            "SELECT user_id AS UserId, updated_at_utc AS UpdatedAtUtc FROM platform_users WHERE auth_subject = @s;",
            new { s = AuthSubject });

        if (baseline.UsersForSubject == 0)
        {
            userRow.Should().BeNull("fail-fast must not upsert actor on end-period-closed path");
        }
        else
        {
            userRow.Should().NotBeNull();
            var actualUser = userRow!;
            actualUser.UserId.Should().Be(baseline.UserId!.Value);
            actualUser.UpdatedAtUtc.Should().Be(baseline.UserUpdatedAtUtc!.Value, "no-op must not touch platform_users.updated_at_utc");
        }

        var expectedDocumentId = DeterministicGuid.Create($"CloseFiscalYear|{FiscalYearEndPeriod:yyyy-MM-dd}");

        (await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM accounting_posting_state WHERE document_id = @id AND operation = 4;",
                new { id = expectedDocumentId }))
            .Should().Be(0, "end-period-closed must fail-fast before creating FY-close posting_log rows");

        (await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM accounting_register_main WHERE document_id = @id;",
                new { id = expectedDocumentId }))
            .Should().Be(0, "end-period-closed must not write accounting register");

        (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM accounting_closed_periods;"))
            .Should().Be(baseline.ClosedPeriods, "CloseFiscalYear must not change closed periods on fail-fast path");
    }

    private sealed record Baseline(
        int AuditEvents,
        int AuditChanges,
        int UsersForSubject,
        Guid? UserId,
        DateTime? UserUpdatedAtUtc,
        int ClosedPeriods);

    private async Task<Baseline> CaptureBaselineAsync()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var eventsCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
        var changesCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");
        var closedPeriods = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM accounting_closed_periods;");

        var user = await conn.QuerySingleOrDefaultAsync<PlatformUserRow>(
            "SELECT user_id AS UserId, updated_at_utc AS UpdatedAtUtc FROM platform_users WHERE auth_subject = @s;",
            new { s = AuthSubject });

        if (user is null)
            return new Baseline(eventsCount, changesCount, UsersForSubject: 0, UserId: null, UserUpdatedAtUtc: null, closedPeriods);

        return new Baseline(eventsCount, changesCount, UsersForSubject: 1, UserId: user.UserId, UserUpdatedAtUtc: user.UpdatedAtUtc, closedPeriods);
    }

    private sealed record AccountIds(Guid CashId, Guid RevenueId, Guid ExpenseId, Guid RetainedEarningsId);

    private static async Task<AccountIds> SeedCoAAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        async Task<Guid> GetOrCreateAsync(string code, string name, AccountType type)
        {
            var existing = (await repo.GetForAdminAsync(includeDeleted: true))
                .FirstOrDefault(a => a.Account.Code == code && !a.IsDeleted);

            if (existing is not null)
            {
                if (!existing.IsActive)
                    await svc.SetActiveAsync(existing.Account.Id, true, CancellationToken.None);

                return existing.Account.Id;
            }

            return await svc.CreateAsync(
                new CreateAccountRequest(
                    Code: code,
                    Name: name,
                    Type: type,
                    IsContra: false,
                    NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                    DimensionRules: Array.Empty<AccountDimensionRuleRequest>()),
                CancellationToken.None);
        }

        var cashId = await GetOrCreateAsync("50", "Cash", AccountType.Asset);
        var revenueId = await GetOrCreateAsync("90.1", "Revenue", AccountType.Income);
        var expenseId = await GetOrCreateAsync("91", "Expenses", AccountType.Expense);
        var retainedId = await GetOrCreateAsync("84", "Retained earnings", AccountType.Equity);

        return new AccountIds(cashId, revenueId, expenseId, retainedId);
    }

    private static async Task PostAsync(
        IHost host,
        Guid documentId,
        DateTime periodUtc,
        string debitCode,
        string creditCode,
        decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            PostingOperation.Post,
            async (ctx, ct) =>
            {
                var coa = await ctx.GetChartOfAccountsAsync(ct);
                var debit = coa.Get(debitCode);
                var credit = coa.Get(creditCode);
                ctx.Post(documentId, periodUtc, debit: debit, credit: credit, amount: amount);
                await Task.CompletedTask;
            },
            manageTransaction: true,
            CancellationToken.None);
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}
