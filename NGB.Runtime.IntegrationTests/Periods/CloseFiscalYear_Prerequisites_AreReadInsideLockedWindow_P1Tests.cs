using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
using NGB.Accounting.PostingState;
using NGB.Persistence.Locks;
using NGB.Persistence.Periods;
using NGB.Persistence.Readers.Periods;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Periods;
using NGB.PostgreSql.Readers;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class CloseFiscalYear_Prerequisites_AreReadInsideLockedWindow_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CloseFiscalYearAsync_DoesNotReadChainOrPriorMonthPrerequisites_WhilePriorMonthLockIsHeld()
    {
        await Fixture.ResetDatabaseAsync();

        var endPeriod = new DateOnly(2042, 12, 1);
        var priorMonth = endPeriod.AddMonths(-1);
        var dec15Utc = new DateTime(2042, 12, 15, 0, 0, 0, DateTimeKind.Utc);
        var dec20Utc = new DateTime(2042, 12, 20, 0, 0, 0, DateTimeKind.Utc);
        var probe = new FiscalYearPrerequisiteReadProbe(priorMonth);

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            services.RemoveAll<IClosedPeriodRepository>();
            services.RemoveAll<IClosedPeriodReader>();

            services.AddSingleton(probe);

            services.AddScoped<PostgresClosedPeriodRepository>();
            services.AddScoped<IClosedPeriodRepository>(sp =>
                new ProbingClosedPeriodRepository(
                    sp.GetRequiredService<PostgresClosedPeriodRepository>(),
                    sp.GetRequiredService<FiscalYearPrerequisiteReadProbe>()));

            services.AddScoped<PostgresClosedPeriodReader>();
            services.AddScoped<IClosedPeriodReader>(sp =>
                new ProbingClosedPeriodReader(
                    sp.GetRequiredService<PostgresClosedPeriodReader>(),
                    sp.GetRequiredService<FiscalYearPrerequisiteReadProbe>()));
        });

        var retainedEarningsId = await SeedCoaAsync(host);

        for (var p = new DateOnly(2042, 1, 1); p < endPeriod; p = p.AddMonths(1))
            await CloseMonthAsync(host, p);

        await PostAsync(host, Guid.CreateVersion7(), dec15Utc, debit: "50", credit: "90.1", amount: 100m);
        await PostAsync(host, Guid.CreateVersion7(), dec20Utc, debit: "91", credit: "50", amount: 40m);

        probe.ResetObservationWindow();

        await using var lockScope = host.Services.CreateAsyncScope();
        var uow = lockScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var locks = lockScope.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();

        await uow.BeginTransactionAsync(CancellationToken.None);
        await locks.LockPeriodAsync(priorMonth, CancellationToken.None);
        probe.SetPriorMonthLockHeld(true);

        var closeTask = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

            await closing.CloseFiscalYearAsync(
                fiscalYearEndPeriod: endPeriod,
                retainedEarningsAccountId: retainedEarningsId,
                closedBy: "test",
                ct: CancellationToken.None);
        });

        await Task.Delay(TimeSpan.FromSeconds(1));

        probe.SawChainReadWhileLockHeld.Should().BeFalse(
            "CloseFiscalYear must not read the closing chain snapshot before it has serialized prerequisite months");
        probe.SawPriorMonthCheckWhileLockHeld.Should().BeFalse(
            "CloseFiscalYear must not read prior-month closed-state prerequisites before it has serialized prerequisite months");

        probe.SetPriorMonthLockHeld(false);
        await uow.CommitAsync(CancellationToken.None);

        await closeTask.WaitAsync(TimeSpan.FromSeconds(10));

        probe.TotalChainReads.Should().BeGreaterThan(0, "CloseFiscalYear should still validate the chain after the prerequisite lock is released.");
        probe.TotalPriorMonthChecks.Should().BeGreaterThan(0, "CloseFiscalYear should still validate prior-month closure prerequisites after the prerequisite lock is released.");
    }

    private static async Task<Guid> SeedCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await svc.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);

        var reId = await svc.CreateAsync(new CreateAccountRequest(
            Code: "300",
            Name: "Retained Earnings",
            Type: AccountType.Equity,
            StatementSection: StatementSection.Equity,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);

        await svc.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);

        await svc.CreateAsync(new CreateAccountRequest(
            Code: "91",
            Name: "Expenses",
            Type: AccountType.Expense,
            StatementSection: StatementSection.Expenses,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);

        return reId;
    }

    private static async Task CloseMonthAsync(IHost host, DateOnly period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
        await closing.CloseMonthAsync(period, closedBy: "test", ct: CancellationToken.None);
    }

    private static async Task PostAsync(
        IHost host,
        Guid documentId,
        DateTime periodUtc,
        string debit,
        string credit,
        decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            PostingOperation.Post,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, periodUtc, chart.Get(debit), chart.Get(credit), amount);
            },
            manageTransaction: true,
            ct: CancellationToken.None);
    }

    private sealed class FiscalYearPrerequisiteReadProbe(DateOnly watchedPriorMonth)
    {
        private int priorMonthLockHeld;
        private int totalChainReads;
        private int totalPriorMonthChecks;
        private int sawChainReadWhileLockHeld;
        private int sawPriorMonthCheckWhileLockHeld;

        public bool SawChainReadWhileLockHeld => Volatile.Read(ref sawChainReadWhileLockHeld) == 1;
        public bool SawPriorMonthCheckWhileLockHeld => Volatile.Read(ref sawPriorMonthCheckWhileLockHeld) == 1;
        public int TotalChainReads => Volatile.Read(ref totalChainReads);
        public int TotalPriorMonthChecks => Volatile.Read(ref totalPriorMonthChecks);

        public void SetPriorMonthLockHeld(bool value)
            => Volatile.Write(ref priorMonthLockHeld, value ? 1 : 0);

        public void ResetObservationWindow()
        {
            Volatile.Write(ref totalChainReads, 0);
            Volatile.Write(ref totalPriorMonthChecks, 0);
            Volatile.Write(ref sawChainReadWhileLockHeld, 0);
            Volatile.Write(ref sawPriorMonthCheckWhileLockHeld, 0);
        }

        public void RecordChainRead()
        {
            Interlocked.Increment(ref totalChainReads);
            if (Volatile.Read(ref priorMonthLockHeld) == 1)
                Volatile.Write(ref sawChainReadWhileLockHeld, 1);
        }

        public void RecordPriorMonthCheck(DateOnly period)
        {
            if (period != watchedPriorMonth)
                return;

            Interlocked.Increment(ref totalPriorMonthChecks);
            if (Volatile.Read(ref priorMonthLockHeld) == 1)
                Volatile.Write(ref sawPriorMonthCheckWhileLockHeld, 1);
        }
    }

    private sealed class ProbingClosedPeriodRepository(
        IClosedPeriodRepository inner,
        FiscalYearPrerequisiteReadProbe probe)
        : IClosedPeriodRepository
    {
        public async Task<bool> IsClosedAsync(DateOnly period, CancellationToken ct = default)
        {
            probe.RecordPriorMonthCheck(period);
            return await inner.IsClosedAsync(period, ct);
        }

        public Task MarkClosedAsync(DateOnly period, string closedBy, DateTime closedAtUtc, CancellationToken ct = default)
            => inner.MarkClosedAsync(period, closedBy, closedAtUtc, ct);

        public Task ReopenAsync(DateOnly period, CancellationToken ct = default)
            => inner.ReopenAsync(period, ct);
    }

    private sealed class ProbingClosedPeriodReader(
        IClosedPeriodReader inner,
        FiscalYearPrerequisiteReadProbe probe)
        : IClosedPeriodReader
    {
        public async Task<IReadOnlyList<ClosedPeriodRecord>> GetClosedAsync(
            DateOnly fromInclusive,
            DateOnly toInclusive,
            CancellationToken ct = default)
        {
            probe.RecordChainRead();
            return await inner.GetClosedAsync(fromInclusive, toInclusive, ct);
        }

        public async Task<DateOnly?> GetLatestClosedPeriodAsync(CancellationToken ct = default)
        {
            probe.RecordChainRead();
            return await inner.GetLatestClosedPeriodAsync(ct);
        }

        public Task<bool> ExistsClosedAfterAsync(DateOnly period, CancellationToken ct = default)
            => inner.ExistsClosedAfterAsync(period, ct);
    }
}
