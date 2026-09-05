using FluentAssertions;
using Moq;
using NGB.Core.AuditLog;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.GeneralJournalEntry;
using NGB.Persistence.OperationalRegisters;
using Xunit;

namespace NGB.PostgreSql.Tests;

public sealed class PersistenceDefaultInterfaceFullCoverageTests
{
    [Fact]
    public async Task CursorPageDefaults_ForwardOffsetAndComputeHasMore()
    {
        var users = new Mock<IPlatformUserRepository> { CallBase = true };
        users.Setup(x => x.GetPageAsync(2, 1, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserPage([User()], 3));

        var userPage = await users.Object.GetCursorPageAsync(
            new PlatformUserPageCursor(2, 3), 1, true);

        userPage.HasMore.Should().BeFalse();
        users.VerifyAll();

        var journal = new Mock<IGeneralJournalEntryUiQueryRepository> { CallBase = true };
        journal.Setup(x => x.GetPageAsync(
                0, 1, "memo", null, null, "active", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneralJournalEntryPageRecord([JournalEntry()], 0, 1, 2));

        var journalPage = await journal.Object.GetCursorPageAsync(
            new GeneralJournalEntryPageCursor(0, 2), 1, "memo", null, null, "active");

        journalPage.HasMore.Should().BeTrue();
        journal.VerifyAll();
    }

    [Fact]
    public async Task OperationalProjectionDefaults_DelegateSchemaReadinessAndCursorPaging()
    {
        var registerId = Guid.NewGuid();
        var balances = new Mock<IOperationalRegisterBalancesStore> { CallBase = true };
        balances.Setup(x => x.EnsureSchemaAsync(registerId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await balances.Object.EnsureReadyForWriteAsync(registerId);
        balances.VerifyAll();

        var turnovers = new Mock<IOperationalRegisterTurnoversStore> { CallBase = true };
        turnovers.Setup(x => x.EnsureSchemaAsync(registerId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await turnovers.Object.EnsureReadyForWriteAsync(registerId);
        turnovers.VerifyAll();

        var reader = new Mock<IOperationalRegisterMovementsQueryReader> { CallBase = true };
        reader.SetupSequence(x => x.GetResourceBalancesByDimensionPageAsync(
                registerId,
                new DateOnly(2026, 8, 1),
                null,
                It.IsAny<Guid>(),
                "amount",
                It.IsAny<int>(),
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(NetPage(total: 2))
            .ReturnsAsync(NetPage(total: 2));
        var dimensionId = Guid.NewGuid();

        var first = await reader.Object.GetResourceBalancesByDimensionCursorAsync(
            registerId, new DateOnly(2026, 8, 1), null, dimensionId, "amount", null, 1);
        var last = await reader.Object.GetResourceBalancesByDimensionCursorAsync(
            registerId,
            new DateOnly(2026, 8, 1),
            null,
            dimensionId,
            "amount",
            new OperationalRegisterDimensionResourceNetCursor(false, Guid.NewGuid(), 1, 2, 1m, 0m),
            1);

        first.HasMore.Should().BeTrue();
        last.HasMore.Should().BeFalse();
        reader.Verify(x => x.GetResourceBalancesByDimensionPageAsync(
            registerId, new DateOnly(2026, 8, 1), null, dimensionId, "amount", 0, 1,
            It.IsAny<CancellationToken>()), Times.Once);
        reader.Verify(x => x.GetResourceBalancesByDimensionPageAsync(
            registerId, new DateOnly(2026, 8, 1), null, dimensionId, "amount", 1, 1,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void PostingReadCache_DefaultPrimeIsANoopForProvidersWithoutPrimingSupport()
    {
        var cache = new Mock<IDocumentPostingReadCache> { CallBase = true };

        var action = () => cache.Object.Prime("document:1", new object());

        action.Should().NotThrow();
    }

    private static PlatformUser User()
    {
        var now = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        return new PlatformUser(Guid.NewGuid(), "subject", "user@example.test", "User", true, now, now);
    }

    private static GeneralJournalEntryListItemRecord JournalEntry() => new(
        Guid.NewGuid(),
        new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        "GJE-1",
        "Entry",
        NGB.Core.Documents.DocumentStatus.Draft,
        false,
        GeneralJournalEntryModels.JournalType.Standard,
        GeneralJournalEntryModels.Source.Manual,
        GeneralJournalEntryModels.ApprovalState.Draft,
        null,
        null,
        null,
        false,
        null,
        null,
        null,
        null);

    private static OperationalRegisterDimensionResourceNetPage NetPage(int total) => new(
        [new OperationalRegisterDimensionResourceNetRow(Guid.NewGuid(), 1m, "Value")],
        total,
        1m,
        0m);
}
