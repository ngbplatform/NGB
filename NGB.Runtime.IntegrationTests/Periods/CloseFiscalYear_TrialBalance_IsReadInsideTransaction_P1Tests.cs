using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.TrialBalance;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using NGB.Runtime.Reporting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class CloseFiscalYear_TrialBalance_IsReadInsideTransaction_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CloseFiscalYearAsync_ReadsTrialBalanceInsideUowTransaction()
    {
        await Fixture.ResetDatabaseAsync();

        var probe = new TrialBalanceTransactionProbe();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            // Replace ITrialBalanceReader with a probing wrapper that asserts an active UoW transaction.
            services.RemoveAll<ITrialBalanceReader>();

            services.AddSingleton(probe);

            // Register the original implementation as a concrete type.
            services.AddScoped<TrialBalanceService>();

            services.AddScoped<ITrialBalanceReader>(sp =>
            {
                var inner = sp.GetRequiredService<TrialBalanceService>();
                var uow = sp.GetRequiredService<IUnitOfWork>();
                var p = sp.GetRequiredService<TrialBalanceTransactionProbe>();
                return new ProbingTrialBalanceReader(inner, uow, p);
            });
        });

        var endPeriod = new DateOnly(2039, 1, 1);
        var dayUtc = new DateTime(2039, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        var retainedEarningsId = await SeedCoaAsync(host);

        // Create P&L activity so fiscal-year close posts entries (not log-only branch).
        await PostAsync(host, Guid.CreateVersion7(), dayUtc, debit: "50", credit: "90.1", amount: 100m);
        await PostAsync(host, Guid.CreateVersion7(), dayUtc, debit: "91", credit: "50", amount: 40m);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

            await closing.CloseFiscalYearAsync(
                fiscalYearEndPeriod: endPeriod,
                retainedEarningsAccountId: retainedEarningsId,
                closedBy: "test",
                ct: CancellationToken.None);
        }

        probe.CallCount.Should().BeGreaterThan(0, "CloseFiscalYear must read Trial Balance to compute closing entries.");
        probe.SawActiveTransaction.Should().BeTrue(
            "Trial Balance must be read inside the same UoW transaction as closing, to avoid TOCTOU races.");
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

    private static async Task PostAsync(
        IHost host,
        Guid documentId,
        DateTime dateUtc,
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
                ctx.Post(documentId, dateUtc, chart.Get(debit), chart.Get(credit), amount);
            },
            manageTransaction: true,
            ct: CancellationToken.None);
    }

    private sealed class TrialBalanceTransactionProbe
    {
        private int callCount;

        public int CallCount => Volatile.Read(ref callCount);

        public bool SawActiveTransaction { get; private set; }

        public void Record(bool hasActiveTransaction)
        {
            Interlocked.Increment(ref callCount);
            if (hasActiveTransaction)
                SawActiveTransaction = true;
        }
    }

    private sealed class ProbingTrialBalanceReader(
        ITrialBalanceReader inner,
        IUnitOfWork uow,
        TrialBalanceTransactionProbe probe)
        : ITrialBalanceReader
    {
        public Task<IReadOnlyList<TrialBalanceRow>> GetAsync(
            DateOnly fromInclusive,
            DateOnly toInclusive,
            CancellationToken ct = default)
            => GetAsync(fromInclusive, toInclusive, dimensionScopes: null, ct);

        public async Task<IReadOnlyList<TrialBalanceRow>> GetAsync(
            DateOnly fromInclusive,
            DateOnly toInclusive,
            DimensionScopeBag? dimensionScopes,
            CancellationToken ct = default)
        {
            var hasTx = uow.HasActiveTransaction && uow.Transaction is not null;
            probe.Record(hasTx);

            if (!hasTx)
                throw new NotSupportedException(
                    "Trial Balance must be read inside an active UnitOfWork transaction for CloseFiscalYear.");

            return await inner.GetAsync(fromInclusive, toInclusive, dimensionScopes, ct);
        }
    }
}
