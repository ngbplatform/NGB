using FluentAssertions;
using Moq;
using NGB.Core.Locks;
using NGB.Persistence.Locks;
using NGB.Runtime.Locks;
using Xunit;

namespace NGB.Runtime.Tests.Locks;

public sealed class AdvisoryLockManagerExtensionsFullCoverageTests
{
    [Fact]
    public async Task Document_locks_validate_arguments_skip_empty_values_and_use_batch_capability()
    {
        await ((Func<Task>)(() => ((IAdvisoryLockManager)null!)
                .LockDocumentsDeterministicallyAsync([])))
            .Should().ThrowAsync<ArgumentNullException>();

        var locks = new Mock<IAdvisoryLockBatchManager>(MockBehavior.Strict);
        await ((Func<Task>)(() => locks.Object.LockDocumentsDeterministicallyAsync(null!)))
            .Should().ThrowAsync<ArgumentNullException>();
        await locks.Object.LockDocumentsDeterministicallyAsync([Guid.Empty, Guid.Empty]);
        locks.VerifyNoOtherCalls();

        var later = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var earlier = Guid.Parse("00000000-0000-0000-0000-000000000001");
        locks.Setup(x => x.LockDocumentsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { earlier, later })),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await locks.Object.LockDocumentsDeterministicallyAsync([later, Guid.Empty, earlier, later]);

        locks.VerifyAll();
    }

    [Fact]
    public async Task Document_locks_fall_back_to_ordered_individual_acquisition()
    {
        var locks = new Mock<IAdvisoryLockManager>(MockBehavior.Strict);
        locks.Setup(x => x.LockDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var later = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var earlier = Guid.Parse("00000000-0000-0000-0000-000000000001");

        await locks.Object.LockDocumentsDeterministicallyAsync([later, earlier, later]);

        locks.Invocations.Select(invocation => (Guid)invocation.Arguments[0])
            .Should().Equal(earlier, later);
    }

    [Fact]
    public async Task Period_locks_validate_arguments_normalize_months_and_use_batch_capability()
    {
        await ((Func<Task>)(() => ((IAdvisoryLockManager)null!)
                .LockPeriodsDeterministicallyAsync([], AdvisoryLockPeriodScope.Accounting)))
            .Should().ThrowAsync<ArgumentNullException>();

        var locks = new Mock<IAdvisoryLockBatchManager>(MockBehavior.Strict);
        await ((Func<Task>)(() => locks.Object.LockPeriodsDeterministicallyAsync(
                null!, AdvisoryLockPeriodScope.Accounting)))
            .Should().ThrowAsync<ArgumentNullException>();
        await locks.Object.LockPeriodsDeterministicallyAsync([], AdvisoryLockPeriodScope.Accounting);
        locks.VerifyNoOtherCalls();

        var january = new DateOnly(2026, 1, 1);
        var february = new DateOnly(2026, 2, 1);
        locks.Setup(x => x.LockPeriodsAsync(
                It.Is<IReadOnlyCollection<DateOnly>>(periods => periods.SequenceEqual(new[] { january, february })),
                AdvisoryLockPeriodScope.OperationalRegister,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await locks.Object.LockPeriodsDeterministicallyAsync(
            [new DateOnly(2026, 2, 28), new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 1)],
            AdvisoryLockPeriodScope.OperationalRegister);

        locks.VerifyAll();
    }

    [Fact]
    public async Task Period_locks_fall_back_to_the_scope_specific_individual_overload()
    {
        var locks = new Mock<IAdvisoryLockManager>(MockBehavior.Strict);
        locks.Setup(x => x.LockPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        locks.Setup(x => x.LockPeriodAsync(
                It.IsAny<DateOnly>(), AdvisoryLockPeriodScope.OperationalRegister, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var january = new DateOnly(2026, 1, 1);

        await locks.Object.LockPeriodsDeterministicallyAsync(
            [new DateOnly(2026, 1, 31)], AdvisoryLockPeriodScope.Accounting);
        await locks.Object.LockPeriodsDeterministicallyAsync(
            [new DateOnly(2026, 1, 15)], AdvisoryLockPeriodScope.OperationalRegister);

        locks.Verify(x => x.LockPeriodAsync(january, It.IsAny<CancellationToken>()), Times.Once);
        locks.Verify(x => x.LockPeriodAsync(
            january, AdvisoryLockPeriodScope.OperationalRegister, It.IsAny<CancellationToken>()), Times.Once);
    }
}
