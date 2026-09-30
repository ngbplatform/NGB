using FluentAssertions;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Accounting.Balances;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Accounting.Tests.Balances;

public sealed class AccountingNegativeBalanceCheckerEdgeCaseTests
{
    [Fact]
    public async Task CheckAsync_NullBalances_ThrowsBeforeLoadingChart()
    {
        var chartProvider = new Mock<IChartOfAccountsProvider>(MockBehavior.Strict);
        var sut = new AccountingNegativeBalanceChecker(chartProvider.Object);

        var act = () => sut.CheckAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<NgbArgumentRequiredException>();
        chartProvider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CheckAsync_MissingAccountWithoutResolver_Throws()
    {
        var accountId = Guid.CreateVersion7();
        var chartProvider = CreateEmptyChartProvider();
        var sut = new AccountingNegativeBalanceChecker(chartProvider.Object);

        var act = () => sut.CheckAsync(
            [CreateBalance(accountId, closingBalance: -1m)],
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<NgbInvariantViolationException>();
        exception.Which.Context["accountId"].Should().Be(accountId);
    }

    [Fact]
    public async Task CheckAsync_ResolvedNonViolatingAccount_ReturnsEmpty()
    {
        var account = new Account(Guid.CreateVersion7(), "1010", "Cash", AccountType.Asset);
        var chartProvider = CreateEmptyChartProvider();
        var resolver = new Mock<IAccountByIdResolver>(MockBehavior.Strict);
        resolver
            .Setup(x => x.GetByIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { account.Id })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Account> { [account.Id] = account });
        var sut = new AccountingNegativeBalanceChecker(chartProvider.Object, resolver.Object);

        var result = await sut.CheckAsync(
            [CreateBalance(account.Id, closingBalance: 1m)],
            CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckAsync_AllowPolicy_IgnoresOtherwiseNegativeBalance()
    {
        var account = new Account(
            Guid.CreateVersion7(),
            "1010",
            "Cash",
            AccountType.Asset,
            negativeBalancePolicy: NegativeBalancePolicy.Allow);
        var chart = new ChartOfAccounts();
        chart.Add(account);
        var provider = new Mock<IChartOfAccountsProvider>(MockBehavior.Strict);
        provider.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(chart);

        var result = await new AccountingNegativeBalanceChecker(provider.Object).CheckAsync(
            [CreateBalance(account.Id, closingBalance: -1m)],
            CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckAsync_CreditNormalAccount_ReportsPositiveClosingBalance()
    {
        var account = new Account(
            Guid.CreateVersion7(),
            "2010",
            "Payables",
            AccountType.Liability,
            negativeBalancePolicy: NegativeBalancePolicy.Warn);
        var chart = new ChartOfAccounts();
        chart.Add(account);
        var provider = new Mock<IChartOfAccountsProvider>(MockBehavior.Strict);
        provider.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(chart);

        var result = await new AccountingNegativeBalanceChecker(provider.Object).CheckAsync(
            [CreateBalance(account.Id, closingBalance: 1m)],
            CancellationToken.None);

        result.Should().ContainSingle().Which.Should().Match<NegativeBalanceViolation>(violation =>
            violation.AccountId == account.Id
            && violation.ClosingBalance == 1m
            && violation.Policy == NegativeBalancePolicy.Warn);
    }

    private static Mock<IChartOfAccountsProvider> CreateEmptyChartProvider()
    {
        var provider = new Mock<IChartOfAccountsProvider>(MockBehavior.Strict);
        provider
            .Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChartOfAccounts());
        return provider;
    }

    private static AccountingBalance CreateBalance(Guid accountId, decimal closingBalance)
        => new()
        {
            Period = new DateOnly(2026, 5, 1),
            AccountId = accountId,
            DimensionSetId = Guid.Empty,
            ClosingBalance = closingBalance
        };
}
