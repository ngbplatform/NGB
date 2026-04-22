using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Runtime.Accounts;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using NGB.Runtime.Periods;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_PeriodClosing_P0Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CloseMonth_WritesAuditEvent_AndSecondCloseDoesNotWriteAnotherEvent()
    {
        var period = new DateOnly(2026, 2, 1);
        var day1Utc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|period-closer-1",
                        Email: "closer1@example.com",
                        DisplayName: "Period Closer 1")));
            });

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), day1Utc, debitCode: "50", creditCode: "90.1", amount: 10m);

        await ReportingTestHelpers.CloseMonthAsync(host, period, closedBy: "closer");

        var second = async () => await ReportingTestHelpers.CloseMonthAsync(host, period, closedBy: "closer2");
        await second.Should().ThrowAsync<PeriodAlreadyClosedException>();

        var expectedEntityId = DeterministicGuid.Create($"CloseMonth|{period:yyyy-MM-dd}");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Period,
                    EntityId: expectedEntityId,
                    ActionCode: AuditActionCodes.PeriodCloseMonth,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().HaveCount(1);
            var ev = events.Single();

            ev.ActorUserId.Should().NotBeNull();
            var user = await users.GetByAuthSubjectAsync("kc|period-closer-1", CancellationToken.None);
            user.Should().NotBeNull();
            user!.UserId.Should().Be(ev.ActorUserId!.Value);

            ev.Changes.Select(c => c.FieldPath)
                .Should()
                .Contain(["is_closed", "closed_by", "closed_at_utc"]);

            ev.Changes.Single(c => c.FieldPath == "is_closed").OldValueJson.Should().Contain("false");
            ev.Changes.Single(c => c.FieldPath == "is_closed").NewValueJson.Should().Contain("true");
            ev.Changes.Single(c => c.FieldPath == "closed_by").NewValueJson.Should().Contain("closer");
        }
    }

    [Fact]
    public async Task CloseFiscalYear_NoClosingEntriesRequired_WritesAuditEvent_AndIsIdempotent()
    {
        var fiscalYearEndPeriod = new DateOnly(2026, 1, 1);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|fy-closer-1",
                        Email: "fy.closer@example.com",
                        DisplayName: "FY Closer")));
            });

        Guid retainedEarningsId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            retainedEarningsId = await coa.CreateAsync(
                new CreateAccountRequest(
                    Code: "300",
                    Name: "Retained Earnings",
                    Type: AccountType.Equity,
                    IsContra: false,
                    NegativeBalancePolicy: NegativeBalancePolicy.Allow),
                CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            await svc.CloseFiscalYearAsync(
                fiscalYearEndPeriod,
                retainedEarningsAccountId: retainedEarningsId,
                closedBy: "fy-closer",
                ct: CancellationToken.None);

            var second = async () => await svc.CloseFiscalYearAsync(
                fiscalYearEndPeriod,
                retainedEarningsAccountId: retainedEarningsId,
                closedBy: "fy-closer-2",
                ct: CancellationToken.None);

            await second.Should().ThrowAsync<FiscalYearAlreadyClosedException>();
        }

        var expectedEntityId = DeterministicGuid.Create($"CloseFiscalYear|{fiscalYearEndPeriod:yyyy-MM-dd}");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Period,
                    EntityId: expectedEntityId,
                    ActionCode: AuditActionCodes.PeriodCloseFiscalYear,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().HaveCount(1);
            var ev = events.Single();

            ev.ActorUserId.Should().NotBeNull();
            var user = await users.GetByAuthSubjectAsync("kc|fy-closer-1", CancellationToken.None);
            user.Should().NotBeNull();
            user!.UserId.Should().Be(ev.ActorUserId!.Value);

            ev.Changes.Select(c => c.FieldPath)
                .Should()
                .Contain([
                    "is_fiscal_year_closed",
                    "fiscal_year_end_period",
                    "retained_earnings_account_id",
                    "closing_entries_posted",
                    "closed_by",
                    "closed_at_utc"
                ]);

            ev.Changes.Single(c => c.FieldPath == "is_fiscal_year_closed").NewValueJson.Should().Contain("true");
            ev.Changes.Single(c => c.FieldPath == "closing_entries_posted").NewValueJson.Should().Contain("false");
            ev.Changes.Single(c => c.FieldPath == "retained_earnings_account_id").NewValueJson.Should().Contain(retainedEarningsId.ToString());
            ev.Changes.Single(c => c.FieldPath == "closed_by").NewValueJson.Should().Contain("fy-closer");
        }
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}
