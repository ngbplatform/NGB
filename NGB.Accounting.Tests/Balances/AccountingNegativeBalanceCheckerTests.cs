using FluentAssertions;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Accounting.Balances;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Accounting.Tests.Balances;

public sealed class AccountingNegativeBalanceCheckerTests
{
    [Fact]
    public async Task CheckAsync_WhenMissingAccountIsReturnedByBatchResolver_DoesNotCallSingleLookup()
    {
        // Arrange
        var account = new Account(
            id: Guid.CreateVersion7(),
            code: "1010",
            name: "Cash clearing",
            type: AccountType.Asset);

        AccountingBalance[] balances =
        [
            new AccountingBalance
            {
                Period = new DateOnly(2026, 3, 1),
                AccountId = account.Id,
                DimensionSetId = Guid.CreateVersion7(),
                ClosingBalance = -10m
            }
        ];

        var chartProvider = new Mock<IChartOfAccountsProvider>(MockBehavior.Strict);
        chartProvider
            .Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChartOfAccounts());

        var resolver = new Mock<IAccountByIdResolver>(MockBehavior.Strict);
        resolver
            .Setup(x => x.GetByIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(account.Id)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Account> { [account.Id] = account });

        var sut = new AccountingNegativeBalanceChecker(chartProvider.Object, resolver.Object);

        // Act
        var result = await sut.CheckAsync(balances, CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
        result[0].AccountId.Should().Be(account.Id);
        result[0].AccountCode.Should().Be(account.Code);

        resolver.Verify(x => x.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        resolver.Verify(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckAsync_WhenBatchResolverMissesAccount_ThrowsWithoutPerAccountFallback()
    {
        // Arrange
        var missingAccountId = Guid.CreateVersion7();
        AccountingBalance[] balances =
        [
            new AccountingBalance
            {
                Period = new DateOnly(2026, 3, 1),
                AccountId = missingAccountId,
                DimensionSetId = Guid.CreateVersion7(),
                ClosingBalance = -25m
            }
        ];

        var chartProvider = new Mock<IChartOfAccountsProvider>(MockBehavior.Strict);
        chartProvider
            .Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChartOfAccounts());

        var resolver = new Mock<IAccountByIdResolver>(MockBehavior.Strict);
        resolver
            .Setup(x => x.GetByIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(missingAccountId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Account>());

        var sut = new AccountingNegativeBalanceChecker(chartProvider.Object, resolver.Object);

        // Act
        var act = () => sut.CheckAsync(balances, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<NgbInvariantViolationException>();
        ex.Which.Context.Should().ContainKey("accountId");
        ex.Which.Context["accountId"].Should().Be(missingAccountId);

        resolver.Verify(x => x.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        resolver.Verify(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckAsync_MixedSnapshotAndResolverPath_OnlyBatchFetchesDistinctMissingNonZeroAccounts()
    {
        // Arrange
        var snapshotAccount = new Account(
            id: Guid.CreateVersion7(),
            code: "1010",
            name: "Cash operating",
            type: AccountType.Asset);

        var missingAccount = new Account(
            id: Guid.CreateVersion7(),
            code: "1020",
            name: "Cash transit",
            type: AccountType.Asset);

        AccountingBalance[] balances =
        [
            new AccountingBalance
            {
                Period = new DateOnly(2026, 3, 1),
                AccountId = snapshotAccount.Id,
                DimensionSetId = Guid.CreateVersion7(),
                ClosingBalance = -10m
            },
            new AccountingBalance
            {
                Period = new DateOnly(2026, 3, 1),
                AccountId = snapshotAccount.Id,
                DimensionSetId = Guid.CreateVersion7(),
                ClosingBalance = 0m
            },
            new AccountingBalance
            {
                Period = new DateOnly(2026, 3, 1),
                AccountId = missingAccount.Id,
                DimensionSetId = Guid.CreateVersion7(),
                ClosingBalance = -20m
            },
            new AccountingBalance
            {
                Period = new DateOnly(2026, 3, 1),
                AccountId = missingAccount.Id,
                DimensionSetId = Guid.CreateVersion7(),
                ClosingBalance = -30m
            },
            new AccountingBalance
            {
                Period = new DateOnly(2026, 3, 1),
                AccountId = Guid.CreateVersion7(),
                DimensionSetId = Guid.CreateVersion7(),
                ClosingBalance = 0m
            }
        ];

        var chart = new ChartOfAccounts();
        chart.Add(snapshotAccount);

        var chartProvider = new Mock<IChartOfAccountsProvider>(MockBehavior.Strict);
        chartProvider
            .Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(chart);

        var resolver = new Mock<IAccountByIdResolver>(MockBehavior.Strict);
        resolver
            .Setup(x => x.GetByIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids =>
                    ids.Count == 1
                    && ids.Contains(missingAccount.Id)
                    && !ids.Contains(snapshotAccount.Id)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Account> { [missingAccount.Id] = missingAccount });

        var sut = new AccountingNegativeBalanceChecker(chartProvider.Object, resolver.Object);

        // Act
        var result = await sut.CheckAsync(balances, CancellationToken.None);

        // Assert
        result.Should().HaveCount(3);
        result.Select(x => x.AccountId).Should().Equal(snapshotAccount.Id, missingAccount.Id, missingAccount.Id);

        resolver.Verify(x => x.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        resolver.Verify(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
