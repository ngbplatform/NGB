using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Readers;
using NGB.Runtime.Accounts;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_PeriodClosing_Metadata_And_ClosingEntries_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CloseMonth_WritesDeterministicEntityId_AndMetadataPeriod()
    {
        await Fixture.ResetDatabaseAsync();

        var period = new DateOnly(2026, 3, 1);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|period-meta-1",
                        Email: "period.meta.1@example.com",
                        DisplayName: "Period Meta 1")));
            });

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            await svc.CloseMonthAsync(period, closedBy: "period-meta-closer", ct: CancellationToken.None);
        }

        var expectedEntityId = DeterministicGuid.Create($"CloseMonth|{period:yyyy-MM-dd}");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Period,
                    EntityId: expectedEntityId,
                    ActionCode: AuditActionCodes.PeriodCloseMonth,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().ContainSingle();
            var ev = events.Single();

            ev.EntityId.Should().Be(expectedEntityId);
            ev.MetadataJson.Should().NotBeNullOrWhiteSpace();
            ev.OccurredAtUtc.Kind.Should().Be(DateTimeKind.Utc);

            using var meta = JsonDocument.Parse(ev.MetadataJson!);
            meta.RootElement.GetProperty("period").GetString().Should().Be("2026-03-01");
        }
    }

    [Fact]
    public async Task CloseFiscalYear_WithClosingEntriesRequired_WritesAuditEvent_WithClosingEntriesPostedTrue_AndMetadataClosingDayUtc()
    {
        await Fixture.ResetDatabaseAsync();

        var fiscalYearEndPeriod = new DateOnly(2026, 1, 1);
        var revenueDayUtc = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var expectedClosingDayUtc = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|fy-closer-movements-1",
                        Email: "fy.movements.1@example.com",
                        DisplayName: "FY Closer Movements 1")));
            });

        Guid retainedEarningsId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            await coa.CreateAsync(new CreateAccountRequest(
                    Code: "50",
                    Name: "Cash",
                    Type: AccountType.Asset,
                    StatementSection: StatementSection.Assets,
                    NegativeBalancePolicy: NegativeBalancePolicy.Allow),
                CancellationToken.None);

            await coa.CreateAsync(new CreateAccountRequest(
                    Code: "90.1",
                    Name: "Revenue",
                    Type: AccountType.Income,
                    StatementSection: StatementSection.Income,
                    NegativeBalancePolicy: NegativeBalancePolicy.Allow),
                CancellationToken.None);

            retainedEarningsId = await coa.CreateAsync(
                new CreateAccountRequest(
                    Code: "300",
                    Name: "Retained Earnings",
                    Type: AccountType.Equity,
                    IsContra: false,
                    NegativeBalancePolicy: NegativeBalancePolicy.Allow),
                CancellationToken.None);
        }

        // Create non-zero P&L activity so that CloseFiscalYear must post closing entries.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();
            var revenueDocId = Guid.CreateVersion7();

            await posting.PostAsync(
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(revenueDocId, revenueDayUtc, chart.Get("50"), chart.Get("90.1"), 123.45m);
                },
                ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            await svc.CloseFiscalYearAsync(
                fiscalYearEndPeriod,
                retainedEarningsAccountId: retainedEarningsId,
                closedBy: "fy-closer-movements",
                ct: CancellationToken.None);

            var second = async () => await svc.CloseFiscalYearAsync(
                fiscalYearEndPeriod,
                retainedEarningsAccountId: retainedEarningsId,
                closedBy: "fy-closer-movements-2",
                ct: CancellationToken.None);

            await second.Should().ThrowAsync<FiscalYearAlreadyClosedException>();
        }

        var expectedEntityId = DeterministicGuid.Create($"CloseFiscalYear|{fiscalYearEndPeriod:yyyy-MM-dd}");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
            var entryReader = scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>();

            // Closing entries must exist for the deterministic CloseFiscalYear document id.
            var entries = await entryReader.GetByDocumentAsync(expectedEntityId, CancellationToken.None);
            entries.Should().NotBeEmpty();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Period,
                    EntityId: expectedEntityId,
                    ActionCode: AuditActionCodes.PeriodCloseFiscalYear,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().ContainSingle();
            var ev = events.Single();

            ev.EntityId.Should().Be(expectedEntityId);

            ev.ActorUserId.Should().NotBeNull();
            var user = await users.GetByAuthSubjectAsync("kc|fy-closer-movements-1", CancellationToken.None);
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

            using (var closingPosted = JsonDocument.Parse(ev.Changes.Single(c => c.FieldPath == "closing_entries_posted").NewValueJson!))
                closingPosted.RootElement.GetBoolean().Should().BeTrue();

            using (var retained = JsonDocument.Parse(ev.Changes.Single(c => c.FieldPath == "retained_earnings_account_id").NewValueJson!))
                retained.RootElement.GetGuid().Should().Be(retainedEarningsId);

            ev.MetadataJson.Should().NotBeNullOrWhiteSpace();
            using (var meta = JsonDocument.Parse(ev.MetadataJson!))
            {
                meta.RootElement.GetProperty("closing_day_utc").GetDateTime().Should().Be(expectedClosingDayUtc);
            }
        }
    }

    [Fact]
    public async Task CloseFiscalYear_NoClosingEntriesRequired_WritesAuditEvent_WithClosingEntriesPostedFalse_AndNoEntries()
    {
        await Fixture.ResetDatabaseAsync();

        var fiscalYearEndPeriod = new DateOnly(2026, 1, 1);
        var expectedClosingDayUtc = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|fy-closer-noop-meta-1",
                        Email: "fy.noop.meta.1@example.com",
                        DisplayName: "FY Closer Noop Meta 1")));
            });

        Guid retainedEarningsId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            retainedEarningsId = await coa.CreateAsync(
                new CreateAccountRequest(
                    Code: "301",
                    Name: "Retained Earnings (noop meta test)",
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
                closedBy: "fy-closer-noop-meta",
                ct: CancellationToken.None);
        }

        var expectedEntityId = DeterministicGuid.Create($"CloseFiscalYear|{fiscalYearEndPeriod:yyyy-MM-dd}");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();
            var entryReader = scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>();

            var entries = await entryReader.GetByDocumentAsync(expectedEntityId, CancellationToken.None);
            entries.Should().BeEmpty();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Period,
                    EntityId: expectedEntityId,
                    ActionCode: AuditActionCodes.PeriodCloseFiscalYear,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().ContainSingle();
            var ev = events.Single();

            using (var closingPosted = JsonDocument.Parse(ev.Changes.Single(c => c.FieldPath == "closing_entries_posted").NewValueJson!))
                closingPosted.RootElement.GetBoolean().Should().BeFalse();

            ev.MetadataJson.Should().NotBeNullOrWhiteSpace();
            using (var meta = JsonDocument.Parse(ev.MetadataJson!))
            {
                meta.RootElement.GetProperty("closing_day_utc").GetDateTime().Should().Be(expectedClosingDayUtc);
            }
        }
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}
