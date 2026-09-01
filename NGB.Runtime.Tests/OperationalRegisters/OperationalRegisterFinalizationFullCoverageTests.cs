using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Core.Locks;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.Locks;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.OperationalRegisters.Projections;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.OperationalRegisters;

public sealed class OperationalRegisterFinalizationFullCoverageTests
{
    private static readonly DateTime Now = new(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Service_GetAndMutationsCoverValidationMissingRegistersAndPeriodChains()
    {
        var f = new ServiceFixture();
        var registerId = Guid.NewGuid();
        var requested = new DateOnly(2026, 3, 27);
        var month = new DateOnly(2026, 3, 1);
        var future = new DateOnly(2026, 5, 1);
        var marker = Finalization(registerId, month, OperationalRegisterFinalizationStatus.Dirty);

        await ((Func<Task>)(() => f.Sut.GetAsync(Guid.Empty, requested)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        f.Finalizations.Setup(x => x.GetAsync(registerId, month, It.IsAny<CancellationToken>()))
            .ReturnsAsync(marker);
        (await f.Sut.GetAsync(registerId, requested)).Should().BeSameAs(marker);

        await ((Func<Task>)(() => f.Sut.MarkDirtyAsync(Guid.Empty, requested)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        f.Registers.Setup(x => x.GetByIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationalRegisterAdminItem?)null);
        await ((Func<Task>)(() => f.Sut.MarkDirtyAsync(registerId, requested)))
            .Should().ThrowAsync<OperationalRegisterNotFoundException>();

        f.Registers.Setup(x => x.GetByIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register(registerId, "Stock"));
        f.Finalizations.Setup(x => x.GetTrackedPeriodsOnOrAfterAsync(registerId, month, It.IsAny<CancellationToken>()))
            .ReturnsAsync([future, month, future]);
        await f.Sut.MarkDirtyAsync(registerId, requested, manageTransaction: false);

        f.Locks.Verify(x => x.LockOperationalRegisterAsync(registerId, It.IsAny<CancellationToken>()), Times.Once);
        f.Locks.Verify(x => x.LockPeriodAsync(month, AdvisoryLockPeriodScope.OperationalRegister, It.IsAny<CancellationToken>()), Times.Once);
        f.Locks.Verify(x => x.LockPeriodAsync(future, AdvisoryLockPeriodScope.OperationalRegister, It.IsAny<CancellationToken>()), Times.Once);
        f.Finalizations.Verify(x => x.MarkDirtyPeriodsAsync(
            registerId,
            It.Is<IReadOnlyCollection<DateOnly>>(periods => periods.SequenceEqual(new[] { month, future })),
            Now,
            Now,
            It.IsAny<CancellationToken>()), Times.Once);

        await ((Func<Task>)(() => f.Sut.MarkFinalizedAsync(Guid.Empty, requested)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        f.Registers.Setup(x => x.GetByIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationalRegisterAdminItem?)null);
        await ((Func<Task>)(() => f.Sut.MarkFinalizedAsync(registerId, requested)))
            .Should().ThrowAsync<OperationalRegisterNotFoundException>();

        f.Registers.Setup(x => x.GetByIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register(registerId, "Stock"));
        await f.Sut.MarkFinalizedAsync(registerId, requested);
        f.Finalizations.Verify(x => x.MarkFinalizedAsync(registerId, month, Now, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Runner_ConstructorRejectsInvalidAndDuplicateProjectorRegistrations()
    {
        var emptyProjector = Projector(" ");
        ((Action)(() => Runner(projectors: [emptyProjector.Object])))
            .Should().Throw<NgbConfigurationViolationException>();

        ((Action)(() => Runner(projectors: [Projector(" SALES ").Object, Projector("sales").Object])))
            .Should().Throw<NgbConfigurationViolationException>();

        ((Action)(() => Runner(legacy: [Legacy("\t").Object])))
            .Should().Throw<NgbConfigurationViolationException>();

        ((Action)(() => Runner(projectors: [Projector("sales").Object], legacy: [Legacy(" Sales ").Object])))
            .Should().Throw<NgbConfigurationViolationException>();

        ((Action)(() => Runner(defaults: [DefaultProjector().Object, DefaultProjector().Object])))
            .Should().Throw<NgbConfigurationViolationException>();
    }

    [Fact]
    public void LegacyAdapter_ExposesUnderlyingNormalizedCode()
    {
        var legacy = Legacy("legacy");
        var adapter = new LegacyFinalizerProjectorAdapter(legacy.Object);

        adapter.RegisterCodeNorm.Should().Be("legacy");
    }

    [Fact]
    public async Task Runner_ValidatesArgumentsAndReturnsZeroForEmptyQueues()
    {
        var finalizations = new Mock<IOperationalRegisterFinalizationRepository>(MockBehavior.Loose);
        finalizations.Setup(x => x.GetDirtyAcrossAllAsync(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        finalizations.Setup(x => x.GetDirtyAsync(It.IsAny<Guid>(), 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var sut = Runner(finalizations: finalizations);

        await ((Func<Task>)(() => sut.FinalizeDirtyAsync(0)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        (await sut.FinalizeDirtyAsync()).Should().Be(0);
        await ((Func<Task>)(() => sut.FinalizeRegisterDirtyAsync(Guid.Empty)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => sut.FinalizeRegisterDirtyAsync(Guid.NewGuid(), -1)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        (await sut.FinalizeRegisterDirtyAsync(Guid.NewGuid())).Should().Be(0);
    }

    [Fact]
    public async Task Runner_SpecificProjectorFinalizesDirtyAndSkipsStaleRows()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var period = new DateOnly(2026, 4, 1);
        var finalizations = new Mock<IOperationalRegisterFinalizationRepository>(MockBehavior.Loose);
        finalizations.Setup(x => x.GetDirtyAcrossAllAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Finalization(firstId, period, OperationalRegisterFinalizationStatus.Dirty),
                Finalization(secondId, period, OperationalRegisterFinalizationStatus.Dirty)
            ]);
        finalizations.Setup(x => x.GetAsync(firstId, period, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Finalization(firstId, period, OperationalRegisterFinalizationStatus.Dirty));
        finalizations.Setup(x => x.GetAsync(secondId, period, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Finalization(secondId, period, OperationalRegisterFinalizationStatus.Finalized));
        var registers = new Mock<IOperationalRegisterRepository>(MockBehavior.Loose);
        registers.Setup(x => x.GetByIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(firstId) && ids.Contains(secondId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([Register(firstId, "  Sales  ")]);
        var projector = Projector("sales");
        OperationalRegisterMonthProjectionContext? observed = null;
        projector.Setup(x => x.RebuildMonthAsync(It.IsAny<OperationalRegisterMonthProjectionContext>(), It.IsAny<CancellationToken>()))
            .Callback<OperationalRegisterMonthProjectionContext, CancellationToken>((ctx, _) => observed = ctx)
            .Returns(Task.CompletedTask);
        var uow = new Mock<IUnitOfWork>(MockBehavior.Loose);
        var movements = new Mock<IOperationalRegisterMovementsReader>().Object;
        var sut = Runner(uow, registers: registers, finalizations: finalizations,
            movements: movements, projectors: [projector.Object]);

        (await sut.FinalizeDirtyAsync(5)).Should().Be(1);

        observed.Should().NotBeNull();
        observed!.RegisterId.Should().Be(firstId);
        observed.RegisterCode.Should().Be("  Sales  ");
        observed.RegisterCodeNorm.Should().Be("sales");
        observed.PeriodMonth.Should().Be(period);
        observed.NowUtc.Should().Be(Now);
        observed.Movements.Should().BeSameAs(movements);
        observed.UnitOfWork.Should().BeSameAs(uow.Object);
        finalizations.Verify(x => x.MarkFinalizedAsync(firstId, period, Now, Now, It.IsAny<CancellationToken>()), Times.Once);
        registers.Verify(x => x.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Runner_FinalizeDirty_ProcessesRegistersInIndependentOrderedPartitions()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var january = new DateOnly(2026, 1, 1);
        var february = january.AddMonths(1);
        var finalizations = new Mock<IOperationalRegisterFinalizationRepository>(MockBehavior.Strict);
        finalizations.Setup(x => x.GetDirtyAcrossAllAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Finalization(firstId, february, OperationalRegisterFinalizationStatus.Dirty),
                Finalization(secondId, january, OperationalRegisterFinalizationStatus.Dirty),
                Finalization(firstId, january, OperationalRegisterFinalizationStatus.Dirty)
            ]);
        var registers = new Mock<IOperationalRegisterRepository>(MockBehavior.Strict);
        registers.Setup(x => x.GetByIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(firstId) && ids.Contains(secondId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([Register(firstId, "first"), Register(secondId, "second")]);
        var factory = new RecordingPartitionProcessorFactory();

        var sut = Runner(
            registers: registers,
            finalizations: finalizations,
            partitionProcessorFactory: factory);

        (await sut.FinalizeDirtyAsync(4)).Should().Be(3);
        factory.Calls.Should().HaveCount(2);
        factory.Calls.Any(call =>
            call.Register is not null && call.Register.RegisterId == firstId &&
            call.Items.Select(item => item.Period).SequenceEqual(new[] { january, february })).Should().BeTrue();
        factory.Calls.Any(call =>
            call.Register is not null && call.Register.RegisterId == secondId &&
            call.Items.Select(item => item.Period).SequenceEqual(new[] { january })).Should().BeTrue();
    }

    [Fact]
    public async Task Runner_NullStaleRowDefaultProjectorAndExternalTransactionCoverAlternatePaths()
    {
        var registerId = Guid.NewGuid();
        var nullId = Guid.NewGuid();
        var period = new DateOnly(2026, 5, 1);
        var finalizations = new Mock<IOperationalRegisterFinalizationRepository>(MockBehavior.Loose);
        finalizations.Setup(x => x.GetDirtyAsync(registerId, 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Finalization(nullId, period, OperationalRegisterFinalizationStatus.Dirty),
                Finalization(registerId, period, OperationalRegisterFinalizationStatus.Dirty)
            ]);
        finalizations.Setup(x => x.GetAsync(nullId, period, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationalRegisterFinalization?)null);
        finalizations.Setup(x => x.GetAsync(registerId, period, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Finalization(registerId, period, OperationalRegisterFinalizationStatus.Dirty));
        var registers = new Mock<IOperationalRegisterRepository>(MockBehavior.Loose);
        registers.Setup(x => x.GetByIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register(registerId, "Unknown"));
        var defaultProjector = DefaultProjector();
        var uow = new Mock<IUnitOfWork>(MockBehavior.Loose);
        var sut = Runner(uow, registers: registers, finalizations: finalizations, defaults: [defaultProjector.Object]);

        (await sut.FinalizeRegisterDirtyAsync(registerId, 7, manageTransaction: false)).Should().Be(1);

        uow.Verify(x => x.EnsureActiveTransaction(), Times.Exactly(2));
        uow.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        defaultProjector.Verify(x => x.RebuildMonthAsync(
            It.Is<OperationalRegisterMonthProjectionContext>(c => c.RegisterCodeNorm == "unknown"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Runner_NoProjectorBlocksMonthAndLegacyAdapterFinalizes()
    {
        var blockedId = Guid.NewGuid();
        var legacyId = Guid.NewGuid();
        var period = new DateOnly(2026, 6, 1);

        var blockedFinalizations = DirtyRepository(blockedId, period);
        var blockedRegisters = RegisterRepository(blockedId, "NoHandler");
        var blocked = Runner(registers: blockedRegisters, finalizations: blockedFinalizations);
        (await blocked.FinalizeRegisterDirtyAsync(blockedId)).Should().Be(0);
        blockedFinalizations.Verify(x => x.MarkBlockedNoProjectorAsync(
            blockedId, period, Now, "no_projector", Now, It.IsAny<CancellationToken>()), Times.Once);

        var legacyFinalizations = DirtyRepository(legacyId, period);
        var legacyRegisters = RegisterRepository(legacyId, " Legacy ");
        var legacy = Legacy("legacy");
        var legacyRunner = Runner(registers: legacyRegisters, finalizations: legacyFinalizations, legacy: [legacy.Object]);
        (await legacyRunner.FinalizeRegisterDirtyAsync(legacyId)).Should().Be(1);
        legacy.Verify(x => x.FinalizeMonthAsync(legacyId, period, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Runner_MissingRegisterAndProjectorFailuresCoverRollbackConditions()
    {
        var registerId = Guid.NewGuid();
        var period = new DateOnly(2026, 7, 1);
        var finalizations = DirtyRepository(registerId, period);
        var missing = new Mock<IOperationalRegisterRepository>(MockBehavior.Loose);
        missing.Setup(x => x.GetByIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationalRegisterAdminItem?)null);
        var activeUow = new Mock<IUnitOfWork>(MockBehavior.Loose);
        activeUow.SetupGet(x => x.HasActiveTransaction).Returns(true);
        var missingRunner = Runner(activeUow, registers: missing, finalizations: finalizations, defaults: [DefaultProjector().Object]);

        await ((Func<Task>)(() => missingRunner.FinalizeRegisterDirtyAsync(registerId)))
            .Should().ThrowAsync<OperationalRegisterNotFoundException>();
        activeUow.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);

        var throwingProjector = Projector("broken");
        throwingProjector.Setup(x => x.RebuildMonthAsync(It.IsAny<OperationalRegisterMonthProjectionContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("projector failed"));
        var inactiveUow = new Mock<IUnitOfWork>(MockBehavior.Loose);
        var failingRunner = Runner(inactiveUow, registers: RegisterRepository(registerId, "broken"),
            finalizations: DirtyRepository(registerId, period), projectors: [throwingProjector.Object]);
        await ((Func<Task>)(() => failingRunner.FinalizeRegisterDirtyAsync(registerId)))
            .Should().ThrowAsync<InvalidOperationException>();
        inactiveUow.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);

        var externalRunner = Runner(registers: RegisterRepository(registerId, "broken"),
            finalizations: DirtyRepository(registerId, period), projectors: [throwingProjector.Object]);
        await ((Func<Task>)(() => externalRunner.FinalizeRegisterDirtyAsync(registerId, manageTransaction: false)))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    private static OperationalRegisterFinalizationService Service(
        Mock<IUnitOfWork> uow,
        Mock<IAdvisoryLockManager> locks,
        Mock<IOperationalRegisterRepository> registers,
        Mock<IOperationalRegisterFinalizationRepository> finalizations)
        => new(uow.Object, locks.Object, registers.Object, finalizations.Object,
            new FixedTimeProvider(Now), NullLogger<OperationalRegisterFinalizationService>.Instance);

    private sealed class ServiceFixture
    {
        public Mock<IUnitOfWork> Uow { get; } = new(MockBehavior.Loose);
        public Mock<IAdvisoryLockManager> Locks { get; } = new(MockBehavior.Loose);
        public Mock<IOperationalRegisterRepository> Registers { get; } = new(MockBehavior.Loose);
        public Mock<IOperationalRegisterFinalizationRepository> Finalizations { get; } = new(MockBehavior.Loose);
        public OperationalRegisterFinalizationService Sut { get; }

        public ServiceFixture() => Sut = Service(Uow, Locks, Registers, Finalizations);
    }

    private static OperationalRegisterFinalizationRunner Runner(
        Mock<IUnitOfWork>? uow = null,
        Mock<IAdvisoryLockManager>? locks = null,
        Mock<IOperationalRegisterRepository>? registers = null,
        Mock<IOperationalRegisterFinalizationRepository>? finalizations = null,
        IOperationalRegisterMovementsReader? movements = null,
        IReadOnlyList<IOperationalRegisterMonthProjector>? projectors = null,
        IReadOnlyList<IOperationalRegisterDefaultMonthProjector>? defaults = null,
        IReadOnlyList<IOperationalRegisterMonthFinalizer>? legacy = null,
        IOperationalRegisterFinalizationPartitionProcessorFactory? partitionProcessorFactory = null)
        => new(
            (uow ?? new Mock<IUnitOfWork>(MockBehavior.Loose)).Object,
            (locks ?? new Mock<IAdvisoryLockManager>(MockBehavior.Loose)).Object,
            (registers ?? new Mock<IOperationalRegisterRepository>(MockBehavior.Loose)).Object,
            (finalizations ?? new Mock<IOperationalRegisterFinalizationRepository>(MockBehavior.Loose)).Object,
            movements ?? new Mock<IOperationalRegisterMovementsReader>(MockBehavior.Loose).Object,
            projectors ?? [], defaults ?? [], legacy ?? [],
            new FixedTimeProvider(Now), NullLogger<OperationalRegisterFinalizationRunner>.Instance,
            partitionProcessorFactory);

    private static Mock<IOperationalRegisterMonthProjector> Projector(string code)
    {
        var mock = new Mock<IOperationalRegisterMonthProjector>(MockBehavior.Loose);
        mock.SetupGet(x => x.RegisterCodeNorm).Returns(code);
        return mock;
    }

    private static Mock<IOperationalRegisterDefaultMonthProjector> DefaultProjector()
        => new(MockBehavior.Loose);

    private static Mock<IOperationalRegisterMonthFinalizer> Legacy(string code)
    {
        var mock = new Mock<IOperationalRegisterMonthFinalizer>(MockBehavior.Loose);
        mock.SetupGet(x => x.RegisterCodeNorm).Returns(code);
        return mock;
    }

    private sealed class RecordingPartitionProcessorFactory
        : IOperationalRegisterFinalizationPartitionProcessorFactory
    {
        public System.Collections.Concurrent.ConcurrentBag<(
            OperationalRegisterAdminItem? Register,
            IReadOnlyList<OperationalRegisterFinalization> Items)> Calls { get; } = [];

        public Task<int> ProcessAsync(
            OperationalRegisterAdminItem? register,
            IReadOnlyList<OperationalRegisterFinalization> items,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Add((register, items));
            return Task.FromResult(items.Count);
        }
    }

    private static Mock<IOperationalRegisterFinalizationRepository> DirtyRepository(Guid registerId, DateOnly period)
    {
        var mock = new Mock<IOperationalRegisterFinalizationRepository>(MockBehavior.Loose);
        var item = Finalization(registerId, period, OperationalRegisterFinalizationStatus.Dirty);
        mock.Setup(x => x.GetDirtyAsync(registerId, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([item]);
        mock.Setup(x => x.GetAsync(registerId, period, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        return mock;
    }

    private static Mock<IOperationalRegisterRepository> RegisterRepository(Guid id, string code)
    {
        var mock = new Mock<IOperationalRegisterRepository>(MockBehavior.Loose);
        mock.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(Register(id, code));
        return mock;
    }

    private static OperationalRegisterAdminItem Register(Guid id, string code)
        => new(id, code, code.Trim().ToLowerInvariant(), "table", "Register", false, Now, Now);

    private static OperationalRegisterFinalization Finalization(
        Guid id, DateOnly period, OperationalRegisterFinalizationStatus status)
        => new(id, period, status, null, Now, null, null, Now, Now);

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
