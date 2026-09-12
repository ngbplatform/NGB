using System.Runtime.CompilerServices;
using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.TrialBalance;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class TrialBalanceReportService_P0Tests
{
    [Fact]
    public async Task Null_request_is_rejected()
    {
        var service = new TrialBalanceReportService(new Reader([]));
        await ((Func<Task>)(() => service.GetPageAsync(null!))).Should().ThrowAsync<NgbArgumentRequiredException>();
    }

    [Fact]
    public async Task Empty_result_has_no_groups_and_zero_totals()
    {
        var page = await new TrialBalanceReportService(new Reader([])).GetPageAsync(new());
        page.Rows.Should().BeEmpty();
        page.Totals.Should().Be(new TrialBalanceReportTotals(0, 0, 0, 0));
    }

    [Theory]
    [InlineData(true, 8)]
    [InlineData(false, 6)]
    public async Task Groups_subtotals_and_totals_preserve_account_values(bool showSubtotals, int count)
    {
        var reader = new Reader([
            new(Guid.NewGuid(), "1000", "Cash", AccountType.Asset, 10, 20, 3),
            new(Guid.NewGuid(), "1100", "Receivables", AccountType.Asset, 5, 7, 4),
            new(Guid.NewGuid(), "4000", "Rent", AccountType.Income, -15, 0, 20),
            new(Guid.NewGuid(), "4010", "Other", AccountType.Income, 0, 0, 0)
        ]);
        var scope = new DimensionScopeBag([new(Guid.NewGuid(), [Guid.NewGuid()])]);
        var page = await new TrialBalanceReportService(reader).GetPageAsync(new()
        {
            FromInclusive = new(2026, 9, 1), ToInclusive = new(2026, 10, 1), DimensionScopes = scope, ShowSubtotals = showSubtotals
        });
        page.Rows.Should().HaveCount(count);
        page.Totals.Should().Be(new TrialBalanceReportTotals(0, 27, 27, 0));
        page.Rows.Where(r => r.RowKind == TrialBalanceReportRowKind.Detail).Select(r => r.ClosingBalance).Should().Equal(27, 8, -35, 0);
        reader.Scopes.Should().BeSameAs(scope);
        reader.From.Should().Be(new DateOnly(2026, 9, 1));
        reader.To.Should().Be(new DateOnly(2026, 10, 1));
        if (showSubtotals)
            page.Rows.Where(r => r.RowKind == TrialBalanceReportRowKind.Subtotal).Select(r => r.ClosingBalance).Should().Equal(35, -35);
    }

    [Fact]
    public async Task More_than_ten_thousand_accounts_are_not_rejected_or_truncated()
    {
        var accounts = Enumerable.Range(0, 10_001).Select(i =>
            new TrialBalanceAccountSummary(Guid.NewGuid(), i.ToString("D5"), "Account", AccountType.Asset, 1, 2, 3)).ToArray();
        var page = await new TrialBalanceReportService(new Reader(accounts)).GetPageAsync(new() { ShowSubtotals = true });
        page.Rows.Count(r => r.RowKind == TrialBalanceReportRowKind.Detail).Should().Be(10_001);
        page.Totals.Should().Be(new TrialBalanceReportTotals(10_001, 20_002, 30_003, 0));
    }

    [Fact]
    public async Task Cancellation_reaches_the_streaming_reader()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = new TrialBalanceReportService(new Reader([]));
        await ((Func<Task>)(() => service.GetPageAsync(new(), cts.Token))).Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class Reader(TrialBalanceAccountSummary[] accounts) : ITrialBalanceAccountSummaryReader
    {
        public DimensionScopeBag? Scopes { get; private set; }
        public DateOnly From { get; private set; }
        public DateOnly To { get; private set; }
        public async IAsyncEnumerable<TrialBalanceAccountSummary> ReadAsync(DateOnly fromInclusive, DateOnly toInclusive,
            DimensionScopeBag? dimensionScopes, [EnumeratorCancellation] CancellationToken ct = default)
        {
            Scopes = dimensionScopes;
            From = fromInclusive;
            To = toInclusive;
            await Task.CompletedTask;
            ct.ThrowIfCancellationRequested();
            foreach (var account in accounts) yield return account;
        }
    }
}
