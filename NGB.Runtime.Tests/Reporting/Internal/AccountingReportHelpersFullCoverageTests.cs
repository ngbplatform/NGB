using FluentAssertions;
using Moq;
using NGB.Accounting.Balances;
using NGB.Accounting.Turnovers;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers;
using NGB.Runtime.Reporting.Internal;
using Xunit;

namespace NGB.Runtime.Tests.Reporting.Internal;

public sealed class AccountingReportHelpersFullCoverageTests
{
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherAccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DimensionId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OtherDimensionId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ValueId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid OtherValueId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    [Fact]
    public async Task ComputeOpeningBalance_NoClosedBalanceAtInception_ReturnsZeroWithoutTurnovers()
    {
        var balances = new Mock<IAccountingBalanceReader>(MockBehavior.Strict);
        balances.Setup(x => x.GetLatestClosedAsync(DateOnly.MinValue, CancellationToken.None))
            .ReturnsAsync(Array.Empty<AccountingBalance>());
        var turnovers = new Mock<IAccountingTurnoverReader>(MockBehavior.Strict);

        var result = await AccountingReportHelpers.ComputeOpeningBalanceAsync(
            AccountId, null, DateOnly.MinValue, balances.Object, turnovers.Object, CancellationToken.None);

        result.Should().Be(0m);
        balances.VerifyAll();
        turnovers.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ComputeOpeningBalance_NoClosedBalance_SumsMatchingHistoricalTurnovers()
    {
        var from = new DateOnly(2026, 4, 1);
        var scope = Scopes((DimensionId, ValueId));
        var balances = new Mock<IAccountingBalanceReader>(MockBehavior.Strict);
        balances.Setup(x => x.GetLatestClosedAsync(from, CancellationToken.None))
            .ReturnsAsync(Array.Empty<AccountingBalance>());
        var turnovers = new Mock<IAccountingTurnoverReader>(MockBehavior.Strict);
        turnovers.Setup(x => x.GetRangeAsync(DateOnly.MinValue, new DateOnly(2026, 3, 1), CancellationToken.None))
            .ReturnsAsync(new[]
            {
                Turnover(AccountId, 10m, 3m, Row((DimensionId, ValueId))),
                Turnover(AccountId, 100m, 0m, Row((DimensionId, OtherValueId))),
                Turnover(OtherAccountId, 100m, 0m, Row((DimensionId, ValueId)))
            });

        var result = await AccountingReportHelpers.ComputeOpeningBalanceAsync(
            AccountId, scope, from, balances.Object, turnovers.Object, CancellationToken.None);

        result.Should().Be(7m);
        balances.VerifyAll();
        turnovers.VerifyAll();
    }

    [Fact]
    public async Task ComputeOpeningBalance_ClosedSamePeriod_UsesMatchingOpeningBalances()
    {
        var period = new DateOnly(2026, 4, 1);
        var balances = new Mock<IAccountingBalanceReader>(MockBehavior.Strict);
        balances.Setup(x => x.GetLatestClosedAsync(period, CancellationToken.None)).ReturnsAsync(new[]
        {
            Balance(period, AccountId, 10m, 11m),
            Balance(period, AccountId, 20m, 22m),
            Balance(period, OtherAccountId, 100m, 100m)
        });
        var turnovers = new Mock<IAccountingTurnoverReader>(MockBehavior.Strict);

        var result = await AccountingReportHelpers.ComputeOpeningBalanceAsync(
            AccountId, DimensionScopeBag.Empty, period, balances.Object, turnovers.Object, CancellationToken.None);

        result.Should().Be(30m);
        balances.VerifyAll();
        turnovers.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ComputeOpeningBalance_NoMatchingClosedRows_ReturnsZeroAtClosedPeriod()
    {
        var period = new DateOnly(2026, 4, 1);
        var balances = new Mock<IAccountingBalanceReader>(MockBehavior.Strict);
        balances.Setup(x => x.GetLatestClosedAsync(period, CancellationToken.None)).ReturnsAsync(new[]
        {
            Balance(period, OtherAccountId, 100m, 100m)
        });
        var turnovers = new Mock<IAccountingTurnoverReader>(MockBehavior.Strict);

        var result = await AccountingReportHelpers.ComputeOpeningBalanceAsync(
            AccountId, null, period, balances.Object, turnovers.Object, CancellationToken.None);

        result.Should().Be(0m);
        turnovers.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ComputeOpeningBalance_AdjacentClosedPeriod_UsesClosingWithoutEmptyRangeRead()
    {
        var closed = new DateOnly(2026, 3, 1);
        var from = new DateOnly(2026, 4, 1);
        var balances = new Mock<IAccountingBalanceReader>(MockBehavior.Strict);
        balances.Setup(x => x.GetLatestClosedAsync(from, CancellationToken.None)).ReturnsAsync(new[]
        {
            Balance(closed, AccountId, 4m, 9m)
        });
        var turnovers = new Mock<IAccountingTurnoverReader>(MockBehavior.Strict);

        var result = await AccountingReportHelpers.ComputeOpeningBalanceAsync(
            AccountId, null, from, balances.Object, turnovers.Object, CancellationToken.None);

        result.Should().Be(9m);
        turnovers.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ComputeOpeningBalance_OlderClosedPeriod_RollsForwardOnlyMatchingTurnovers()
    {
        var closed = new DateOnly(2026, 1, 1);
        var from = new DateOnly(2026, 4, 1);
        var scope = Scopes((DimensionId, ValueId));
        var balances = new Mock<IAccountingBalanceReader>(MockBehavior.Strict);
        balances.Setup(x => x.GetLatestClosedAsync(from, CancellationToken.None)).ReturnsAsync(new[]
        {
            Balance(closed, AccountId, 1m, 5m, Row((DimensionId, ValueId)))
        });
        var turnovers = new Mock<IAccountingTurnoverReader>(MockBehavior.Strict);
        turnovers.Setup(x => x.GetRangeAsync(new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1), CancellationToken.None))
            .ReturnsAsync(new[]
            {
                Turnover(AccountId, 9m, 2m, Row((DimensionId, ValueId))),
                Turnover(AccountId, 50m, 0m, DimensionBag.Empty),
                Turnover(OtherAccountId, 50m, 0m, Row((DimensionId, ValueId)))
            });

        var result = await AccountingReportHelpers.ComputeOpeningBalanceAsync(
            AccountId, scope, from, balances.Object, turnovers.Object, CancellationToken.None);

        result.Should().Be(12m);
        balances.VerifyAll();
        turnovers.VerifyAll();
    }

    [Fact]
    public void ScopeMatching_CoversEmptyRowsMissingDimensionsWrongValuesAndAndSemantics()
    {
        var oneScope = Scopes((DimensionId, ValueId));
        AccountingReportHelpers.MatchesScopes(DimensionBag.Empty, oneScope).Should().BeFalse();
        AccountingReportHelpers.MatchesScopes(Row((OtherDimensionId, ValueId)), oneScope).Should().BeFalse();
        AccountingReportHelpers.MatchesScopes(Row((DimensionId, OtherValueId)), oneScope).Should().BeFalse();
        AccountingReportHelpers.MatchesScopes(Row((DimensionId, ValueId)), oneScope).Should().BeTrue();

        var twoScopes = Scopes((DimensionId, ValueId), (OtherDimensionId, OtherValueId));
        AccountingReportHelpers.MatchesScopes(Row((DimensionId, ValueId)), twoScopes).Should().BeFalse();
        AccountingReportHelpers.MatchesScopes(
            Row((DimensionId, ValueId), (OtherDimensionId, OtherValueId)), twoScopes).Should().BeTrue();
    }

    [Fact]
    public void MatchesEitherSide_CoversShortCircuitCreditFallbackAndNoMatch()
    {
        var scopes = Scopes((DimensionId, ValueId));
        var matching = Row((DimensionId, ValueId));
        var missing = Row((OtherDimensionId, OtherValueId));

        AccountingReportHelpers.MatchesEitherSide(matching, DimensionBag.Empty, scopes).Should().BeTrue();
        AccountingReportHelpers.MatchesEitherSide(missing, matching, scopes).Should().BeTrue();
        AccountingReportHelpers.MatchesEitherSide(missing, DimensionBag.Empty, scopes).Should().BeFalse();
    }

    private static AccountingBalance Balance(
        DateOnly period,
        Guid accountId,
        decimal opening,
        decimal closing,
        DimensionBag? dimensions = null) => new()
    {
        Period = period,
        AccountId = accountId,
        OpeningBalance = opening,
        ClosingBalance = closing,
        Dimensions = dimensions ?? DimensionBag.Empty
    };

    private static AccountingTurnover Turnover(
        Guid accountId,
        decimal debit,
        decimal credit,
        DimensionBag dimensions) => new()
    {
        AccountId = accountId,
        DebitAmount = debit,
        CreditAmount = credit,
        Dimensions = dimensions
    };

    private static DimensionBag Row(params (Guid DimensionId, Guid ValueId)[] values) =>
        new(values.Select(x => new DimensionValue(x.DimensionId, x.ValueId)));

    private static DimensionScopeBag Scopes(params (Guid DimensionId, Guid ValueId)[] values) =>
        new(values.Select(x => new DimensionScope(x.DimensionId, [x.ValueId])));
}
