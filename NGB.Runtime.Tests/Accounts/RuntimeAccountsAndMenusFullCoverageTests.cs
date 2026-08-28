using FluentAssertions;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Admin;
using NGB.Persistence.Accounts;
using NGB.Runtime.Accounts;
using NGB.Runtime.Admin;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Accounts;

public sealed class RuntimeAccountsAndMenusFullCoverageTests
{
    [Fact]
    public async Task AccountByIdResolver_CoversMissingDeletedActiveNullEmptyAndBatchFiltering()
    {
        var active = Item("1000");
        var deleted = Item("2000", deleted: true);
        var repository = new Mock<IChartOfAccountsRepository>(MockBehavior.Strict);
        repository.SetupSequence(x => x.GetAdminByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChartOfAccountsAdminItem?)null)
            .ReturnsAsync(deleted)
            .ReturnsAsync(active);
        repository.Setup(x => x.GetAdminByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([deleted, active, active]);
        var sut = new AccountByIdResolver(repository.Object);

        (await sut.GetByIdAsync(Guid.CreateVersion7())).Should().BeNull();
        (await sut.GetByIdAsync(deleted.Account.Id)).Should().BeNull();
        (await sut.GetByIdAsync(active.Account.Id)).Should().BeSameAs(active.Account);
        var nullAct = () => sut.GetByIdsAsync(null!);
        await nullAct.Should().ThrowAsync<NgbArgumentRequiredException>();
        (await sut.GetByIdsAsync([])).Should().BeEmpty();
        var map = await sut.GetByIdsAsync([deleted.Account.Id, active.Account.Id]);
        map.Should().ContainSingle().Which.Should().Be(new KeyValuePair<Guid, Account>(active.Account.Id, active.Account));
    }

    [Fact]
    public async Task AdminService_DelegatesBothReadShapesAndPreservesCancellation()
    {
        var item = Item("1000");
        using var source = new CancellationTokenSource();
        var repository = new Mock<IChartOfAccountsRepository>(MockBehavior.Strict);
        repository.Setup(x => x.GetForAdminAsync(true, source.Token)).ReturnsAsync([item]);
        repository.Setup(x => x.GetAdminByIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { item.Account.Id })), source.Token))
            .ReturnsAsync([item]);
        var sut = new ChartOfAccountsAdminService(repository.Object);

        (await sut.GetAsync(true, source.Token)).Should().Equal(item);
        (await sut.GetByIdsAsync([item.Account.Id], source.Token)).Should().Equal(item);
        repository.VerifyAll();
    }

    [Fact]
    public async Task Provider_LoadsOnceAndWaitingCallerReusesCache()
    {
        var account = Item("1000").Account;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Mock<IChartOfAccountsRepository>(MockBehavior.Strict);
        repository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                entered.SetResult();
                await release.Task;
                return [account];
            });
        var sut = new ChartOfAccountsProvider(repository.Object, new ChartOfAccountsSnapshotCache());

        var first = sut.GetAsync();
        await entered.Task;
        var waiting = sut.GetAsync();
        release.SetResult();
        var charts = await Task.WhenAll(first, waiting);

        charts[0].Should().BeSameAs(charts[1]);
        (await sut.GetAsync()).Should().BeSameAs(charts[0]);
        charts[0].Get("1000").Should().BeSameAs(account);
        repository.Verify(x => x.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SharedProviderCache_ReusesSnapshotAcrossScopesAndReloadsAfterInvalidation()
    {
        var firstAccount = Item("1000").Account;
        var secondAccount = Item("2000").Account;
        var repository = new Mock<IChartOfAccountsRepository>(MockBehavior.Strict);
        repository.SetupSequence(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([firstAccount])
            .ReturnsAsync([firstAccount, secondAccount]);
        var cache = new ChartOfAccountsSnapshotCache();

        var firstProvider = new ChartOfAccountsProvider(repository.Object, cache);
        var first = await firstProvider.GetAsync();
        var secondProvider = new ChartOfAccountsProvider(repository.Object, cache);
        (await secondProvider.GetAsync()).Should().BeSameAs(first);

        cache.Invalidate();
        var thirdProvider = new ChartOfAccountsProvider(repository.Object, cache);
        var refreshed = await thirdProvider.GetAsync();

        refreshed.Should().NotBeSameAs(first);
        refreshed.Get("2000").Should().BeSameAs(secondAccount);
        (await firstProvider.GetAsync()).Should().BeSameAs(first);
        repository.Verify(x => x.GetAllAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SharedProviderCache_DoesNotPublishLoadThatRacedWithInvalidation()
    {
        var stale = new ChartOfAccounts();
        var fresh = new ChartOfAccounts();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new ChartOfAccountsSnapshotCache();

        var staleLoad = cache.GetOrLoadAsync(async _ =>
        {
            entered.SetResult();
            await release.Task;
            return stale;
        }, default);

        await entered.Task;
        cache.Invalidate();
        release.SetResult();
        (await staleLoad).Should().BeSameAs(stale);
        (await cache.GetOrLoadAsync(_ => Task.FromResult(fresh), default)).Should().BeSameAs(fresh);
        (await cache.GetOrLoadAsync(_ => throw new InvalidOperationException(), default)).Should().BeSameAs(fresh);

        await ((Func<Task>)(() => cache.GetOrLoadAsync(null!, default)))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PlatformMenuContributors_ReturnEveryOwnedMenuItem()
    {
        var admin = await new AccountingAdminMainMenuContributor().ContributeAsync(default);
        var documents = await new AccountingDocumentsMainMenuContributor().ContributeAsync(default);
        var reports = await new AccountingReportsMainMenuContributor().ContributeAsync(default);

        admin.Should().ContainSingle().Which.Items.Should().HaveCount(4);
        documents.Should().ContainSingle().Which.Items.Should().ContainSingle();
        reports.Should().ContainSingle().Which.Items.Should().HaveCount(9);
        reports[0].Items.Select(x => x.Code).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task MainMenuService_CoversEmptyMergeDeduplicationIconsAndOrdering()
    {
        var empty = new MainMenuService([
            new Contributor(null!),
            new Contributor([])
        ]);
        (await empty.GetMainMenuAsync(default)).Groups.Should().BeEmpty();

        var first = ItemDto("document", "invoice", "Later", 20);
        var replacement = ItemDto(" Document ", " INVOICE ", "Earlier", 10);
        var equalDuplicate = ItemDto("document", "invoice", "Ignored", 10);
        var second = ItemDto("catalog", "customer", "Customer", 10);
        var sut = new MainMenuService([
            new Contributor([
                Group(" ", [first]),
                Group("Accounting", [first], ordinal: 50, icon: null),
                Group("Empty", [], ordinal: 1, icon: null),
                Group("BlankIcon", [second], ordinal: 30, icon: null)
            ]),
            new Contributor([
                Group("accounting", [replacement, equalDuplicate, second], ordinal: 20, icon: "calculator"),
                Group("BlankIcon", [second], ordinal: 20, icon: " ")
            ]),
            new Contributor([
                Group("Accounting", [second], ordinal: 40, icon: "other")
            ])
        ]);

        var menu = await sut.GetMainMenuAsync(default);

        menu.Groups.Select(x => x.Label).Should().Equal("Accounting", "BlankIcon");
        menu.Groups[0].Ordinal.Should().Be(20);
        menu.Groups[0].Icon.Should().Be("calculator");
        menu.Groups[0].Items.Select(x => x.Label).Should().Equal("Customer", "Earlier");
        menu.Groups[1].Icon.Should().BeNull();
    }

    private static ChartOfAccountsAdminItem Item(string code, bool deleted = false) => new()
    {
        Account = new Account(Guid.CreateVersion7(), code, code, AccountType.Asset),
        IsActive = !deleted,
        IsDeleted = deleted
    };

    private static MainMenuItemDto ItemDto(string kind, string code, string label, int ordinal) =>
        new(kind, code, label, "/route", null, ordinal);

    private static MainMenuGroupDto Group(
        string label,
        IReadOnlyList<MainMenuItemDto> items,
        int ordinal = 10,
        string? icon = null) => new(label, items, ordinal, icon);

    private sealed class Contributor(IReadOnlyList<MainMenuGroupDto> groups) : IMainMenuContributor
    {
        public Task<IReadOnlyList<MainMenuGroupDto>> ContributeAsync(CancellationToken ct) =>
            Task.FromResult(groups);
    }
}
