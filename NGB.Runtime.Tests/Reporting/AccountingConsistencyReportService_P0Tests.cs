using FluentAssertions;
using NGB.Accounting.Reports.AccountingConsistency;
using NGB.Persistence.Checkers;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class AccountingConsistencyReportService_P0Tests
{
    private static readonly DateOnly Period = new(2026, 2, 1);
    private static readonly DateOnly PreviousPeriod = new(2026, 1, 1);

    [Fact]
    public async Task RunForPeriodAsync_WhenCurrentSnapshotMissesPreviousKey_FlagsBalanceChainMismatch()
    {
        var accountA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var accountB = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var service = new AccountingConsistencyReportService(
            new StubIntegrityDiagnostics(0),
            new StubAccountingConsistencySnapshotReader(
                new AccountingConsistencySnapshot([
                    SnapshotRow(accountA, "1000", opening: 100m, closing: 100m, previousClosing: 100m, hasCurrentBalanceRow: true, hasPreviousBalanceRow: true),
                    SnapshotRow(accountB, "1100", previousClosing: 55m, hasPreviousBalanceRow: true)
                ])));

        var report = await service.RunForPeriodAsync(Period, PreviousPeriod, CancellationToken.None);

        report.IsOk.Should().BeFalse();
        report.BalanceChainMismatchCount.Should().Be(1);
        report.Issues.Should().ContainSingle(x =>
            x.Kind == AccountingConsistencyIssueKind.BalanceChainMismatch
            && x.AccountId == accountB
            && x.DimensionSetId == Guid.Empty
            && x.Message.Contains("Previous closing=55")
            && x.Message.Contains("current opening=0"));
    }

    [Fact]
    public async Task RunForPeriodAsync_WhenCurrentPeriodHasNoBalances_DoesNotFlagChainMismatch()
    {
        var accountId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var service = new AccountingConsistencyReportService(
            new StubIntegrityDiagnostics(0),
            new StubAccountingConsistencySnapshotReader(
                new AccountingConsistencySnapshot([
                    SnapshotRow(accountId, "1000", previousClosing: 100m, hasPreviousBalanceRow: true)
                ])));

        var report = await service.RunForPeriodAsync(Period, PreviousPeriod, CancellationToken.None);

        report.IsOk.Should().BeTrue();
        report.BalanceChainMismatchCount.Should().Be(0);
        report.Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task RunForPeriodAsync_WhenPreviousSnapshotMissing_UsesZeroAsPreviousClosing()
    {
        var accountId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        var service = new AccountingConsistencyReportService(
            new StubIntegrityDiagnostics(0),
            new StubAccountingConsistencySnapshotReader(
                new AccountingConsistencySnapshot([
                    SnapshotRow(accountId, "1000", opening: 12m, closing: 12m, hasCurrentBalanceRow: true)
                ])));

        var report = await service.RunForPeriodAsync(Period, PreviousPeriod, CancellationToken.None);

        report.IsOk.Should().BeFalse();
        report.BalanceChainMismatchCount.Should().Be(1);
        report.Issues.Should().ContainSingle(x =>
            x.Kind == AccountingConsistencyIssueKind.BalanceChainMismatch
            && x.AccountId == accountId
            && x.Message.Contains("Previous closing=0")
            && x.Message.Contains("current opening=12"));
    }

    [Fact]
    public async Task RunForPeriodAsync_WhenPeriodHasNoBalances_MissingKeyCheckDoesNotFire()
    {
        var accountId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        var service = new AccountingConsistencyReportService(
            new StubIntegrityDiagnostics(0),
            new StubAccountingConsistencySnapshotReader(
                new AccountingConsistencySnapshot([
                    SnapshotRow(accountId, "1000", debit: 10m, hasTurnoverRow: true)
                ])));

        var report = await service.RunForPeriodAsync(Period, null, CancellationToken.None);

        report.IsOk.Should().BeTrue();
        report.MissingKeyCount.Should().Be(0);
        report.Issues.Should().BeEmpty();
    }

    private static AccountingConsistencySnapshotRow SnapshotRow(
        Guid accountId,
        string accountCode,
        decimal opening = 0m,
        decimal closing = 0m,
        decimal debit = 0m,
        decimal credit = 0m,
        decimal previousClosing = 0m,
        bool hasCurrentBalanceRow = false,
        bool hasTurnoverRow = false,
        bool hasPreviousBalanceRow = false)
        => new(
            accountId,
            accountCode,
            Guid.Empty,
            opening,
            closing,
            debit,
            credit,
            previousClosing,
            hasCurrentBalanceRow,
            hasTurnoverRow,
            hasPreviousBalanceRow);

    private sealed class StubIntegrityDiagnostics(long diffCount) : IAccountingIntegrityDiagnostics
    {
        public Task<long> GetTurnoversVsRegisterDiffCountAsync(DateOnly period, CancellationToken ct = default)
            => Task.FromResult(diffCount);
    }

    private sealed class StubAccountingConsistencySnapshotReader(AccountingConsistencySnapshot snapshot) : IAccountingConsistencySnapshotReader
    {
        public Task<AccountingConsistencySnapshot> GetAsync(
            DateOnly period,
            DateOnly? previousPeriodForChainCheck = null,
            CancellationToken ct = default)
            => Task.FromResult(snapshot);
    }
}
