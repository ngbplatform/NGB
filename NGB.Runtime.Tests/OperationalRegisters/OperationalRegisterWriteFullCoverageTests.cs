using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Accounting.PostingState;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Core.Locks;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Documents;
using NGB.Persistence.Locks;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.OperationalRegisters;

public sealed class OperationalRegisterWriteFullCoverageTests
{
    private static readonly DateTime Now = new(2026, 8, 15, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task WriteEngine_ValidatesRequiredArgumentsAndReportsMissingEntities()
    {
        var f = new WriteFixture();
        var registerId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        Func<CancellationToken, Task> action = _ => Task.CompletedTask;

        await ((Func<Task>)(() => f.Sut.ExecuteAsync(Guid.Empty, documentId,
                OperationalRegisterWriteOperation.Post, [], action)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => f.Sut.ExecuteAsync(registerId, Guid.Empty,
                OperationalRegisterWriteOperation.Post, [], action)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => f.Sut.ExecuteAsync(registerId, documentId,
                OperationalRegisterWriteOperation.Post, [], null!)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();

        f.Registers.Setup(x => x.GetByIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationalRegisterAdminItem?)null);
        await ((Func<Task>)(() => f.Sut.ExecuteAsync(registerId, documentId,
                OperationalRegisterWriteOperation.Post, null, action)))
            .Should().ThrowAsync<OperationalRegisterNotFoundException>();

        f.Registers.Setup(x => x.GetByIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register(registerId));
        f.Documents.Setup(x => x.GetAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null);
        await ((Func<Task>)(() => f.Sut.ExecuteAsync(registerId, documentId,
                OperationalRegisterWriteOperation.Post, null, action)))
            .Should().ThrowAsync<DocumentNotFoundException>();
    }

    [Fact]
    public async Task WriteEngine_CoversIdempotentAndInProgressOutcomes()
    {
        var f = new WriteFixture();
        var registerId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        f.EntitiesExist(registerId, documentId);
        f.WriteLog.SetupSequence(x => x.TryBeginAsync(registerId, documentId,
                OperationalRegisterWriteOperation.Post, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostingStateBeginResult.AlreadyCompleted)
            .ReturnsAsync(PostingStateBeginResult.InProgress);
        var calls = 0;
        Func<CancellationToken, Task> action = _ => { calls++; return Task.CompletedTask; };

        (await f.Sut.ExecuteAsync(registerId, documentId, OperationalRegisterWriteOperation.Post,
            null, action)).Should().Be(OperationalRegisterWriteResult.AlreadyCompleted);
        await ((Func<Task>)(() => f.Sut.ExecuteAsync(registerId, documentId,
                OperationalRegisterWriteOperation.Post, [], action)))
            .Should().ThrowAsync<OperationalRegisterWriteAlreadyInProgressException>();

        calls.Should().Be(0);
        f.WriteLog.Verify(x => x.MarkCompletedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<OperationalRegisterWriteOperation>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WriteEngine_ExecutesNormalizesLocksAndInvalidatesAffectedAndFutureMonths()
    {
        var f = new WriteFixture();
        var registerId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var february = new DateOnly(2026, 2, 1);
        var april = new DateOnly(2026, 4, 1);
        f.EntitiesExist(registerId, documentId);
        f.Finalizations.Setup(x => x.GetTrackedPeriodsOnOrAfterAsync(registerId, february, It.IsAny<CancellationToken>()))
            .ReturnsAsync([april, february, april]);
        f.WriteLog.Setup(x => x.TryBeginAsync(registerId, documentId,
                OperationalRegisterWriteOperation.Repost, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostingStateBeginResult.Begun);
        var actionCalls = 0;

        var result = await f.Sut.ExecuteAsync(registerId, documentId, OperationalRegisterWriteOperation.Repost,
            [new DateOnly(2026, 2, 28), new DateOnly(2026, 2, 1)],
            _ => { actionCalls++; return Task.CompletedTask; }, manageTransaction: false);

        result.Should().Be(OperationalRegisterWriteResult.Executed);
        actionCalls.Should().Be(1);
        f.Locks.Verify(x => x.LockDocumentAsync(documentId, It.IsAny<CancellationToken>()), Times.Once);
        f.Locks.Verify(x => x.LockOperationalRegisterAsync(registerId, It.IsAny<CancellationToken>()), Times.Once);
        f.Locks.Verify(x => x.LockPeriodAsync(february, AdvisoryLockPeriodScope.OperationalRegister, It.IsAny<CancellationToken>()), Times.Once);
        f.Locks.Verify(x => x.LockPeriodAsync(april, AdvisoryLockPeriodScope.OperationalRegister, It.IsAny<CancellationToken>()), Times.Once);
        f.Finalizations.Verify(x => x.MarkDirtyPeriodsAsync(
            registerId,
            It.Is<IReadOnlyCollection<DateOnly>>(periods => periods.SequenceEqual(new[] { february, april })),
            Now,
            Now,
            It.IsAny<CancellationToken>()), Times.Once);
        f.WriteLog.Verify(x => x.MarkCompletedAsync(registerId, documentId,
            OperationalRegisterWriteOperation.Repost, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WriteEngine_ExecutesWithoutPeriodsAndSkipsRegisterWideLockAndDirtyMarkers()
    {
        var f = new WriteFixture();
        var registerId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        f.EntitiesExist(registerId, documentId);
        f.WriteLog.Setup(x => x.TryBeginAsync(registerId, documentId,
                OperationalRegisterWriteOperation.Unpost, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostingStateBeginResult.Begun);

        (await f.Sut.ExecuteAsync(registerId, documentId, OperationalRegisterWriteOperation.Unpost,
            [], _ => Task.CompletedTask)).Should().Be(OperationalRegisterWriteResult.Executed);

        f.Locks.Verify(x => x.LockOperationalRegisterAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        f.Finalizations.Verify(x => x.MarkDirtyPeriodsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<DateOnly>>(),
            It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MovementsApplier_ValidatesNullIdsUtcAndDocumentOwnershipBeforeWriting()
    {
        var f = new ApplierFixture();
        var registerId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        await ((Func<Task>)(() => f.Sut.ApplyMovementsForDocumentAsync(registerId, documentId,
                OperationalRegisterWriteOperation.Post, null!)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.ApplyMovementsForDocumentAsync(Guid.Empty, documentId,
                OperationalRegisterWriteOperation.Post, [])))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => f.Sut.ApplyMovementsForDocumentAsync(registerId, Guid.Empty,
                OperationalRegisterWriteOperation.Post, [])))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();

        var local = Movement(documentId, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local));
        await ((Func<Task>)(() => f.Sut.ApplyMovementsForDocumentAsync(registerId, documentId,
                OperationalRegisterWriteOperation.Post, [local])))
            .Should().ThrowAsync<NgbArgumentInvalidException>();

        var wrongOwner = Movement(Guid.NewGuid(), Now);
        await ((Func<Task>)(() => f.Sut.ApplyMovementsForDocumentAsync(registerId, documentId,
                OperationalRegisterWriteOperation.Post, [wrongOwner])))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        f.Engine.Calls.Should().Be(0);
    }

    [Fact]
    public async Task MovementsApplier_RejectsExtraDimensionsWithAndWithoutRulesAndMissingRequiredDimensions()
    {
        var registerId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var setId = Guid.NewGuid();
        var allowedDimension = Guid.NewGuid();
        var extraDimension = Guid.NewGuid();
        var movement = Movement(documentId, Now, setId);

        var noRules = new ApplierFixture();
        noRules.Rules.Setup(x => x.GetByRegisterIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        noRules.Bags.Setup(x => x.GetBagsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DimensionBag>
            {
                [setId] = Bag(extraDimension)
            });
        await ((Func<Task>)(() => noRules.Sut.ApplyMovementsForDocumentAsync(registerId, documentId,
                OperationalRegisterWriteOperation.Post, [movement])))
            .Should().ThrowAsync<NgbArgumentInvalidException>();

        var extra = new ApplierFixture();
        extra.Rules.Setup(x => x.GetByRegisterIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new OperationalRegisterDimensionRule(allowedDimension, "allowed", 0, false)]);
        extra.Bags.Setup(x => x.GetBagsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DimensionBag>
            {
                [setId] = Bag(extraDimension)
            });
        await ((Func<Task>)(() => extra.Sut.ApplyMovementsForDocumentAsync(registerId, documentId,
                OperationalRegisterWriteOperation.Repost, [movement])))
            .Should().ThrowAsync<NgbArgumentInvalidException>();

        var missing = new ApplierFixture();
        missing.Rules.Setup(x => x.GetByRegisterIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new OperationalRegisterDimensionRule(allowedDimension, "z_required", 0, true),
                new OperationalRegisterDimensionRule(extraDimension, "a_required", 1, true)
            ]);
        missing.Bags.Setup(x => x.GetBagsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DimensionBag>());
        var assertion = await ((Func<Task>)(() => missing.Sut.ApplyMovementsForDocumentAsync(registerId, documentId,
                OperationalRegisterWriteOperation.Post, [movement])))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        assertion.Which.Message.Should().Contain("a_required, z_required");
    }

    [Fact]
    public async Task MovementsApplier_PostCoversEmptyAndValidDimensionSetsAndDerivedMonths()
    {
        var registerId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var dimensionId = Guid.NewGuid();
        var setId = Guid.NewGuid();
        var f = new ApplierFixture();

        (await f.Sut.ApplyMovementsForDocumentAsync(registerId, documentId,
            OperationalRegisterWriteOperation.Post, [])).Should().Be(OperationalRegisterWriteResult.Executed);
        f.Engine.Periods.Should().BeEmpty();

        f.Rules.Setup(x => x.GetByRegisterIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new OperationalRegisterDimensionRule(dimensionId, "department", 0, true)]);
        f.Bags.Setup(x => x.GetBagsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DimensionBag> { [setId] = Bag(dimensionId) });
        var movements = new[]
        {
            Movement(documentId, new DateTime(2026, 1, 31, 23, 0, 0, DateTimeKind.Utc), setId),
            Movement(documentId, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), setId)
        };

        (await f.Sut.ApplyMovementsForDocumentAsync(registerId, documentId,
            OperationalRegisterWriteOperation.Post, movements)).Should().Be(OperationalRegisterWriteResult.Executed);

        f.Engine.Periods.Should().BeEquivalentTo([new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1)]);
        f.Movements.Verify(x => x.EnsureReadyForWriteAsync(registerId, It.IsAny<CancellationToken>()), Times.Exactly(2));
        f.Movements.Verify(x => x.AppendAsync(registerId, movements, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MovementsApplier_UnpostAndRepostCoverExplicitDerivedAndExternalTransactionPaths()
    {
        var registerId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var march = new DateOnly(2026, 3, 1);
        var april = new DateOnly(2026, 4, 1);
        var f = new ApplierFixture();
        f.Reader.Setup(x => x.GetDistinctMonthsByDocumentAsync(registerId, documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([march, april]);

        (await f.Sut.ApplyMovementsForDocumentAsync(registerId, documentId,
            OperationalRegisterWriteOperation.Unpost, [], affectedPeriods: [new DateOnly(2026, 9, 23)]))
            .Should().Be(OperationalRegisterWriteResult.Executed);
        f.Engine.Periods.Should().Equal(new DateOnly(2026, 9, 23));
        f.Reader.Verify(x => x.GetDistinctMonthsByDocumentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);

        var movement = Movement(documentId, new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc));
        f.Engine.Result = OperationalRegisterWriteResult.AlreadyCompleted;
        (await f.Sut.ApplyMovementsForDocumentAsync(registerId, documentId,
            OperationalRegisterWriteOperation.Repost, [movement], manageTransaction: false))
            .Should().Be(OperationalRegisterWriteResult.AlreadyCompleted);

        f.Engine.Periods.Should().BeEquivalentTo([march, april, new DateOnly(2026, 5, 1)]);
        f.Movements.Verify(x => x.EnsureReadyForWriteAsync(registerId, It.IsAny<CancellationToken>()), Times.Exactly(2));
        f.Movements.Verify(x => x.AppendStornoByDocumentAsync(registerId, documentId, It.IsAny<CancellationToken>()), Times.Exactly(2));
        f.Movements.Verify(x => x.AppendAsync(registerId, It.IsAny<IReadOnlyList<OperationalRegisterMovement>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MovementsApplier_UnknownOperationThrowsFromWriteAction()
    {
        var f = new ApplierFixture();
        await ((Func<Task>)(() => f.Sut.ApplyMovementsForDocumentAsync(Guid.NewGuid(), Guid.NewGuid(),
                (OperationalRegisterWriteOperation)999, [])))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }

    private sealed class WriteFixture
    {
        public Mock<IUnitOfWork> Uow { get; } = new(MockBehavior.Loose);
        public Mock<IAdvisoryLockManager> Locks { get; } = new(MockBehavior.Loose);
        public Mock<IOperationalRegisterRepository> Registers { get; } = new(MockBehavior.Loose);
        public Mock<IDocumentRepository> Documents { get; } = new(MockBehavior.Loose);
        public Mock<IOperationalRegisterWriteStateRepository> WriteLog { get; } = new(MockBehavior.Loose);
        public Mock<IOperationalRegisterFinalizationRepository> Finalizations { get; } = new(MockBehavior.Loose);
        public OperationalRegisterWriteEngine Sut { get; }

        public WriteFixture()
        {
            Sut = new OperationalRegisterWriteEngine(Uow.Object, Locks.Object, Registers.Object,
                Documents.Object, WriteLog.Object, Finalizations.Object, new FixedTimeProvider(Now),
                NullLogger<OperationalRegisterWriteEngine>.Instance);
        }

        public void EntitiesExist(Guid registerId, Guid documentId)
        {
            Registers.Setup(x => x.GetByIdAsync(registerId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Register(registerId));
            Documents.Setup(x => x.GetAsync(documentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Document(documentId));
        }
    }

    private sealed class ApplierFixture
    {
        public RecordingWriteEngine Engine { get; } = new();
        public Mock<IOperationalRegisterMovementsStore> Movements { get; } = new(MockBehavior.Loose);
        public Mock<IOperationalRegisterMovementsReader> Reader { get; } = new(MockBehavior.Loose);
        public Mock<IOperationalRegisterDimensionRuleRepository> Rules { get; } = new(MockBehavior.Loose);
        public Mock<IDimensionSetReader> Bags { get; } = new(MockBehavior.Loose);
        public OperationalRegisterMovementsApplier Sut { get; }

        public ApplierFixture()
        {
            Rules.Setup(x => x.GetByRegisterIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            Bags.Setup(x => x.GetBagsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, DimensionBag>());
            Sut = new OperationalRegisterMovementsApplier(Engine, Movements.Object, Reader.Object, Rules.Object, Bags.Object);
        }
    }

    private sealed class RecordingWriteEngine : IOperationalRegisterWriteEngine
    {
        public int Calls { get; private set; }
        public IReadOnlyCollection<DateOnly>? Periods { get; private set; }
        public OperationalRegisterWriteResult Result { get; set; } = OperationalRegisterWriteResult.Executed;

        public async Task<OperationalRegisterWriteResult> ExecuteAsync(
            Guid registerId,
            Guid documentId,
            OperationalRegisterWriteOperation operation,
            IReadOnlyCollection<DateOnly>? affectedPeriods,
            Func<CancellationToken, Task> writeAction,
            bool manageTransaction = true,
            CancellationToken ct = default)
        {
            Calls++;
            Periods = affectedPeriods;
            await writeAction(ct);
            return Result;
        }
    }

    private static OperationalRegisterAdminItem Register(Guid id)
        => new(id, "stock", "stock", "stock", "Stock", false, Now, Now);

    private static DocumentRecord Document(Guid id)
        => new()
        {
            Id = id,
            TypeCode = "test",
            Number = "T-1",
            DateUtc = Now,
            Status = DocumentStatus.Draft,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };

    private static OperationalRegisterMovement Movement(Guid documentId, DateTime occurredAtUtc, Guid? setId = null)
        => new(documentId, occurredAtUtc, setId ?? Guid.Empty, new Dictionary<string, decimal>());

    private static DimensionBag Bag(Guid dimensionId)
        => new([new DimensionValue(dimensionId, Guid.NewGuid())]);

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
