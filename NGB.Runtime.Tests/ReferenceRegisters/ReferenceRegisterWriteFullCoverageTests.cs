using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Accounting.PostingState;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Documents;
using NGB.Persistence.Locks;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.ReferenceRegisters;

public sealed class ReferenceRegisterWriteFullCoverageTests
{
    private static readonly DateTime Now = new(2026, 8, 15, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task WriteEngine_ValidatesArgumentsAndMissingEntities()
    {
        var f = new WriteFixture();
        var registerId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        Func<CancellationToken, Task> action = _ => Task.CompletedTask;
        await ((Func<Task>)(() => f.Sut.ExecuteAsync(Guid.Empty, documentId,
            ReferenceRegisterWriteOperation.Post, action))).Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => f.Sut.ExecuteAsync(registerId, Guid.Empty,
            ReferenceRegisterWriteOperation.Post, action))).Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => f.Sut.ExecuteAsync(registerId, documentId,
            ReferenceRegisterWriteOperation.Post, null!))).Should().ThrowAsync<NgbArgumentRequiredException>();

        f.Registers.Setup(x => x.GetByIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReferenceRegisterAdminItem?)null);
        await ((Func<Task>)(() => f.Sut.ExecuteAsync(registerId, documentId,
            ReferenceRegisterWriteOperation.Post, action))).Should().ThrowAsync<ReferenceRegisterNotFoundException>();

        f.Registers.Setup(x => x.GetByIdAsync(registerId, It.IsAny<CancellationToken>())).ReturnsAsync(Register(registerId));
        f.Documents.Setup(x => x.GetAsync(documentId, It.IsAny<CancellationToken>())).ReturnsAsync((DocumentRecord?)null);
        await ((Func<Task>)(() => f.Sut.ExecuteAsync(registerId, documentId,
            ReferenceRegisterWriteOperation.Post, action))).Should().ThrowAsync<ReferenceRegisterDocumentNotFoundException>();
    }

    [Fact]
    public async Task WriteEngine_CoversAlreadyCompletedInProgressAndExecutedExternalTransaction()
    {
        var f = new WriteFixture();
        var registerId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        f.EntitiesExist(registerId, documentId);
        f.WriteLog.SetupSequence(x => x.TryBeginAsync(registerId, documentId,
                ReferenceRegisterWriteOperation.Repost, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostingStateBeginResult.AlreadyCompleted)
            .ReturnsAsync(PostingStateBeginResult.InProgress)
            .ReturnsAsync(PostingStateBeginResult.Begun);
        var calls = 0;
        Func<CancellationToken, Task> action = _ => { calls++; return Task.CompletedTask; };

        (await f.Sut.ExecuteAsync(registerId, documentId, ReferenceRegisterWriteOperation.Repost, action))
            .Should().Be(ReferenceRegisterWriteResult.AlreadyCompleted);
        await ((Func<Task>)(() => f.Sut.ExecuteAsync(registerId, documentId,
            ReferenceRegisterWriteOperation.Repost, action))).Should().ThrowAsync<ReferenceRegisterWriteAlreadyInProgressException>();
        (await f.Sut.ExecuteAsync(registerId, documentId, ReferenceRegisterWriteOperation.Repost,
            action, manageTransaction: false)).Should().Be(ReferenceRegisterWriteResult.Executed);

        calls.Should().Be(1);
        f.Locks.Verify(x => x.LockDocumentAsync(documentId, It.IsAny<CancellationToken>()), Times.Exactly(3));
        f.WriteLog.Verify(x => x.MarkCompletedAsync(registerId, documentId,
            ReferenceRegisterWriteOperation.Repost, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordsApplier_ValidatesNullIdsUtcAndRecorderOwnership()
    {
        var f = new ApplierFixture();
        var registerId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        await ((Func<Task>)(() => f.Sut.ApplyRecordsForDocumentAsync(registerId, documentId,
            ReferenceRegisterWriteOperation.Post, null!))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.ApplyRecordsForDocumentAsync(Guid.Empty, documentId,
            ReferenceRegisterWriteOperation.Post, []))).Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => f.Sut.ApplyRecordsForDocumentAsync(registerId, Guid.Empty,
            ReferenceRegisterWriteOperation.Post, []))).Should().ThrowAsync<NgbArgumentOutOfRangeException>();

        var local = Record(period: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local));
        await ((Func<Task>)(() => f.Sut.ApplyRecordsForDocumentAsync(registerId, documentId,
            ReferenceRegisterWriteOperation.Post, [local]))).Should().ThrowAsync<NgbArgumentInvalidException>();

        var wrong = Record(recorder: Guid.NewGuid());
        var assertion = await ((Func<Task>)(() => f.Sut.ApplyRecordsForDocumentAsync(registerId, documentId,
            ReferenceRegisterWriteOperation.Unpost, [wrong]))).Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
        assertion.Which.Reason.Should().Be("recorder_document_id_mismatch");
    }

    [Fact]
    public async Task RecordsApplier_RejectsExtraAndMissingDimensionsAcrossAllValidationBranches()
    {
        var registerId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var setId = Guid.NewGuid();
        var allowed = Guid.NewGuid();
        var extra = Guid.NewGuid();
        var record = Record(setId);

        var noRules = new ApplierFixture();
        noRules.Bags.Setup(x => x.GetBagsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DimensionBag> { [setId] = Bag(extra) });
        await AssertRecordsValidation(noRules.Sut, registerId, documentId, [record], "dimension_not_allowed");

        var extraRules = new ApplierFixture();
        extraRules.Rules.Setup(x => x.GetByRegisterIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ReferenceRegisterDimensionRule(allowed, "allowed", 1, false)]);
        extraRules.Bags.Setup(x => x.GetBagsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DimensionBag> { [setId] = Bag(extra) });
        await AssertRecordsValidation(extraRules.Sut, registerId, documentId, [record], "extra_dimensions",
            ReferenceRegisterWriteOperation.Repost);

        var required = new ApplierFixture();
        required.Rules.Setup(x => x.GetByRegisterIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ReferenceRegisterDimensionRule(allowed, "z_required", 1, true),
                new ReferenceRegisterDimensionRule(extra, "a_required", 2, true)
            ]);
        required.Bags.Setup(x => x.GetBagsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DimensionBag>());
        var missing = await ((Func<Task>)(() => required.Sut.ApplyRecordsForDocumentAsync(registerId, documentId,
            ReferenceRegisterWriteOperation.Post, [record]))).Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
        missing.Which.Reason.Should().Be("missing_required_dimensions");
    }

    [Fact]
    public async Task RecordsApplier_CoversEmptyValidMissingBagNullPeriodRecorderAndUnpostBypass()
    {
        var registerId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var dimension = Guid.NewGuid();
        var setId = Guid.NewGuid();
        var f = new ApplierFixture();

        (await f.Sut.ApplyRecordsForDocumentAsync(registerId, documentId,
            ReferenceRegisterWriteOperation.Post, [])).Should().Be(ReferenceRegisterWriteResult.Executed);

        f.Rules.Setup(x => x.GetByRegisterIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ReferenceRegisterDimensionRule(dimension, "department", 1, true)]);
        f.Bags.Setup(x => x.GetBagsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DimensionBag> { [setId] = Bag(dimension) });
        var valid = new[] { Record(setId, Now, documentId), Record(setId, null, null) };
        f.Engine.Result = ReferenceRegisterWriteResult.AlreadyCompleted;
        (await f.Sut.ApplyRecordsForDocumentAsync(registerId, documentId,
            ReferenceRegisterWriteOperation.Repost, valid, manageTransaction: false))
            .Should().Be(ReferenceRegisterWriteResult.AlreadyCompleted);

        var bypass = Record(Guid.NewGuid());
        await f.Sut.ApplyRecordsForDocumentAsync(registerId, documentId,
            ReferenceRegisterWriteOperation.Unpost, [bypass]);
        f.Rules.Verify(x => x.GetByRegisterIdAsync(registerId, It.IsAny<CancellationToken>()), Times.Once);
        f.Store.Verify(x => x.AppendAsync(registerId, It.IsAny<IReadOnlyList<ReferenceRegisterRecordWrite>>(),
            It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    private static async Task AssertRecordsValidation(
        ReferenceRegisterRecordsApplier sut, Guid registerId, Guid documentId,
        IReadOnlyList<ReferenceRegisterRecordWrite> records, string reason,
        ReferenceRegisterWriteOperation operation = ReferenceRegisterWriteOperation.Post)
    {
        var assertion = await ((Func<Task>)(() => sut.ApplyRecordsForDocumentAsync(registerId, documentId,
            operation, records))).Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
        assertion.Which.Reason.Should().Be(reason);
    }

    private sealed class WriteFixture
    {
        public Mock<IUnitOfWork> Uow { get; } = new(MockBehavior.Loose);
        public Mock<IAdvisoryLockManager> Locks { get; } = new(MockBehavior.Loose);
        public Mock<IReferenceRegisterRepository> Registers { get; } = new(MockBehavior.Loose);
        public Mock<IDocumentRepository> Documents { get; } = new(MockBehavior.Loose);
        public Mock<IReferenceRegisterWriteStateRepository> WriteLog { get; } = new(MockBehavior.Loose);
        public ReferenceRegisterWriteEngine Sut { get; }

        public WriteFixture() => Sut = new(Uow.Object, Locks.Object, Registers.Object, Documents.Object,
            WriteLog.Object, NullLogger<ReferenceRegisterWriteEngine>.Instance, new FixedTimeProvider(Now));

        public void EntitiesExist(Guid registerId, Guid documentId)
        {
            Registers.Setup(x => x.GetByIdAsync(registerId, It.IsAny<CancellationToken>())).ReturnsAsync(Register(registerId));
            Documents.Setup(x => x.GetAsync(documentId, It.IsAny<CancellationToken>())).ReturnsAsync(Document(documentId));
        }
    }

    private sealed class ApplierFixture
    {
        public RecordingEngine Engine { get; } = new();
        public Mock<IReferenceRegisterRecordsStore> Store { get; } = new(MockBehavior.Loose);
        public Mock<IReferenceRegisterDimensionRuleRepository> Rules { get; } = new(MockBehavior.Loose);
        public Mock<IDimensionSetReader> Bags { get; } = new(MockBehavior.Loose);
        public ReferenceRegisterRecordsApplier Sut { get; }

        public ApplierFixture()
        {
            Rules.Setup(x => x.GetByRegisterIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Bags.Setup(x => x.GetBagsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, DimensionBag>());
            Sut = new(Engine, Store.Object, Rules.Object, Bags.Object);
        }
    }

    private sealed class RecordingEngine : IReferenceRegisterWriteEngine
    {
        public ReferenceRegisterWriteResult Result { get; set; } = ReferenceRegisterWriteResult.Executed;
        public async Task<ReferenceRegisterWriteResult> ExecuteAsync(Guid registerId, Guid documentId,
            ReferenceRegisterWriteOperation operation, Func<CancellationToken, Task> writeAction,
            bool manageTransaction = true, CancellationToken ct = default)
        {
            await writeAction(ct);
            return Result;
        }
    }

    private static ReferenceRegisterAdminItem Register(Guid id)
        => new(id, "prices", "prices", "prices", "Prices", ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.SubordinateToRecorder, false, Now, Now);

    private static DocumentRecord Document(Guid id)
        => new() { Id = id, TypeCode = "test", DateUtc = Now, Status = DocumentStatus.Draft,
            CreatedAtUtc = Now, UpdatedAtUtc = Now };

    private static ReferenceRegisterRecordWrite Record(
        Guid? setId = null, DateTime? period = null, Guid? recorder = null)
        => new(setId ?? Guid.Empty, period, recorder, new Dictionary<string, object?>());

    private static DimensionBag Bag(Guid dimension)
        => new([new DimensionValue(dimension, Guid.NewGuid())]);

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
