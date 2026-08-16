using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Accounting.PostingState;
using NGB.Core.AuditLog;
using NGB.Core.Dimensions;
using NGB.Persistence.Dimensions;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Dimensions;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.ReferenceRegisters;

public sealed class ReferenceRegisterIndependentWriteFullCoverageTests
{
    private static readonly DateTime Now = new(2026, 8, 15, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PublicMethods_ValidateNullPayloadsAndUtcArguments()
    {
        var f = new Fixture();
        await ((Func<Task>)(() => f.Sut.UpsertAsync(Guid.NewGuid(), null!, null,
            new Dictionary<string, object?>(), Guid.NewGuid()))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.UpsertAsync(Guid.NewGuid(), [], null,
            null!, Guid.NewGuid()))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.UpsertByDimensionSetIdAsync(Guid.NewGuid(), Guid.Empty, null,
            null!, Guid.NewGuid()))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.TombstoneAsync(Guid.NewGuid(), null!, Now,
            Guid.NewGuid()))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.TombstoneAsync(Guid.NewGuid(), [], LocalTime(),
            Guid.NewGuid()))).Should().ThrowAsync<NgbArgumentInvalidException>();
        await ((Func<Task>)(() => f.Sut.TombstoneByDimensionSetIdAsync(Guid.NewGuid(), Guid.Empty,
            LocalTime(), Guid.NewGuid()))).Should().ThrowAsync<NgbArgumentInvalidException>();
    }

    [Fact]
    public async Task CoreMethods_ValidateRegisterCommandPeriodMissingAndWrongRecordMode()
    {
        var values = new Dictionary<string, object?>();
        var f = new Fixture();
        await ((Func<Task>)(() => f.Sut.UpsertByDimensionSetIdAsync(Guid.Empty, Guid.Empty, null,
            values, Guid.NewGuid()))).Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => f.Sut.UpsertByDimensionSetIdAsync(f.RegisterId, Guid.Empty, null,
            values, Guid.Empty))).Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => f.Sut.UpsertByDimensionSetIdAsync(f.RegisterId, Guid.Empty, LocalTime(),
            values, Guid.NewGuid()))).Should().ThrowAsync<NgbArgumentInvalidException>();
        await ((Func<Task>)(() => f.Sut.TombstoneByDimensionSetIdAsync(Guid.Empty, Guid.Empty, Now,
            Guid.NewGuid()))).Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => f.Sut.TombstoneByDimensionSetIdAsync(f.RegisterId, Guid.Empty, Now,
            Guid.Empty))).Should().ThrowAsync<NgbArgumentOutOfRangeException>();

        var missing = new Fixture(missing: true);
        await ((Func<Task>)(() => missing.Sut.UpsertByDimensionSetIdAsync(missing.RegisterId, Guid.Empty,
            null, values, Guid.NewGuid()))).Should().ThrowAsync<ReferenceRegisterNotFoundException>();

        var subordinate = new Fixture(Register(Guid.NewGuid(), ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.SubordinateToRecorder));
        var assertion = await ((Func<Task>)(() => subordinate.Sut.TombstoneByDimensionSetIdAsync(
            subordinate.RegisterId, Guid.Empty, Now, Guid.NewGuid())))
            .Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
        assertion.Which.Reason.Should().Be("record_mode_not_independent");
    }

    [Fact]
    public async Task Upsert_WrapperCreatesDimensionSetAndCoversAlreadyCompletedAndInProgress()
    {
        var f = new Fixture();
        var dimension = new DimensionValue(Guid.NewGuid(), Guid.NewGuid());
        var setId = Guid.NewGuid();
        var command1 = Guid.NewGuid();
        var command2 = Guid.NewGuid();
        f.DimensionSets.Setup(x => x.GetOrCreateIdAsync(It.IsAny<DimensionBag>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(setId);
        f.WriteLog.SetupSequence(x => x.TryBeginAsync(f.RegisterId, It.IsAny<Guid>(),
                ReferenceRegisterIndependentWriteOperation.Upsert, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostingStateBeginResult.AlreadyCompleted)
            .ReturnsAsync(PostingStateBeginResult.InProgress);

        (await f.Sut.UpsertAsync(f.RegisterId, [dimension], null,
            new Dictionary<string, object?>(), command1)).Should().Be(ReferenceRegisterWriteResult.AlreadyCompleted);
        await ((Func<Task>)(() => f.Sut.UpsertAsync(f.RegisterId, [dimension], null,
            new Dictionary<string, object?>(), command2, manageTransaction: false)))
            .Should().ThrowAsync<ReferenceRegisterIndependentWriteAlreadyInProgressException>();

        f.KeyLock.Verify(x => x.LockKeyAsync(f.RegisterId, setId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Upsert_ValidatesDimensionSetBranches()
    {
        var setId = Guid.NewGuid();
        var allowed = Guid.NewGuid();
        var extra = Guid.NewGuid();
        var values = new Dictionary<string, object?>();

        var noRules = new Fixture();
        noRules.Bags.Setup(x => x.GetBagsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DimensionBag> { [setId] = Bag(extra) });
        await AssertValidation(noRules.Sut.UpsertByDimensionSetIdAsync(noRules.RegisterId, setId, null, values, Guid.NewGuid()),
            "dimension_not_allowed");

        var extraRules = new Fixture();
        extraRules.Rules.Setup(x => x.GetByRegisterIdAsync(extraRules.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ReferenceRegisterDimensionRule(allowed, "allowed", 1, false)]);
        extraRules.Bags.Setup(x => x.GetBagsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DimensionBag> { [setId] = Bag(extra) });
        await AssertValidation(extraRules.Sut.UpsertByDimensionSetIdAsync(extraRules.RegisterId, setId, null, values, Guid.NewGuid()),
            "extra_dimensions");

        var required = new Fixture();
        required.Rules.Setup(x => x.GetByRegisterIdAsync(required.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ReferenceRegisterDimensionRule(allowed, "z", 1, true),
                new ReferenceRegisterDimensionRule(extra, "a", 2, true)
            ]);
        required.Bags.Setup(x => x.GetBagsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DimensionBag>());
        await AssertValidation(required.Sut.UpsertByDimensionSetIdAsync(required.RegisterId, setId, null, values, Guid.NewGuid()),
            "missing_required_dimensions");

        var valid = new Fixture();
        valid.Rules.Setup(x => x.GetByRegisterIdAsync(valid.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ReferenceRegisterDimensionRule(allowed, "allowed", 1, true)]);
        valid.Bags.Setup(x => x.GetBagsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DimensionBag> { [setId] = Bag(allowed) });
        (await valid.Sut.UpsertByDimensionSetIdAsync(valid.RegisterId, setId, null, values, Guid.NewGuid()))
            .Should().Be(ReferenceRegisterWriteResult.Executed);
    }

    [Fact]
    public async Task Upsert_RejectsPeriodicityMismatchesAndExecutesNonPeriodicAndPeriodicAudits()
    {
        var values = new Dictionary<string, object?> { ["amount"] = 10m };
        var nonPeriodicMismatch = new Fixture();
        await AssertValidation(nonPeriodicMismatch.Sut.UpsertByDimensionSetIdAsync(
            nonPeriodicMismatch.RegisterId, Guid.Empty, Now, values, Guid.NewGuid()),
            "period_not_allowed_for_non_periodic");

        var periodicMismatch = new Fixture(Register(Guid.NewGuid(), ReferenceRegisterPeriodicity.Month));
        await AssertValidation(periodicMismatch.Sut.UpsertByDimensionSetIdAsync(
            periodicMismatch.RegisterId, Guid.Empty, null, values, Guid.NewGuid()),
            "period_required_for_periodic");

        var old = Record(Guid.Empty, null, deleted: true);
        var nonPeriodic = new Fixture();
        nonPeriodic.Reader.Setup(x => x.SliceLastForEffectiveMomentAsync(nonPeriodic.RegisterId, Guid.Empty,
            Now, Now, null, It.IsAny<CancellationToken>())).ReturnsAsync(old);
        IReadOnlyList<AuditFieldChange>? nonPeriodicChanges = null;
        CaptureAudit(nonPeriodic.Audit, c => nonPeriodicChanges = c);
        (await nonPeriodic.Sut.UpsertByDimensionSetIdAsync(nonPeriodic.RegisterId, Guid.Empty, null,
            values, Guid.NewGuid())).Should().Be(ReferenceRegisterWriteResult.Executed);
        nonPeriodicChanges.Should().HaveCount(2);

        var period = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodic = new Fixture(Register(Guid.NewGuid(), ReferenceRegisterPeriodicity.Month));
        periodic.Reader.Setup(x => x.SliceLastForEffectiveMomentAsync(periodic.RegisterId, Guid.Empty,
            period, Now, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Record(Guid.Empty, period.AddMonths(-1), deleted: false));
        IReadOnlyList<AuditFieldChange>? periodicChanges = null;
        CaptureAudit(periodic.Audit, c => periodicChanges = c);
        await periodic.Sut.UpsertByDimensionSetIdAsync(periodic.RegisterId, Guid.Empty, period,
            values, Guid.NewGuid(), manageTransaction: false);
        periodic.Reader.Verify(x => x.SliceLastForEffectiveMomentAsync(periodic.RegisterId, Guid.Empty,
            period, Now, null, It.IsAny<CancellationToken>()), Times.Once);
        periodicChanges.Should().HaveCount(3);
        periodic.Store.Verify(x => x.AppendAsync(periodic.RegisterId,
            It.Is<IReadOnlyList<ReferenceRegisterRecordWrite>>(r => r.Count == 1 && !r[0].IsDeleted && r[0].PeriodUtc == period),
            It.IsAny<CancellationToken>()), Times.Once);

        var periodicWithoutOld = new Fixture(Register(Guid.NewGuid(), ReferenceRegisterPeriodicity.Month));
        await periodicWithoutOld.Sut.UpsertByDimensionSetIdAsync(periodicWithoutOld.RegisterId, Guid.Empty,
            period, values, Guid.NewGuid());
    }

    [Fact]
    public async Task Tombstone_WrapperCoversIdempotentInProgressNoRecordDeletedAndActiveRecord()
    {
        var setId = Guid.NewGuid();
        var f = new Fixture();
        f.DimensionSets.Setup(x => x.GetOrCreateIdAsync(It.IsAny<DimensionBag>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(setId);
        f.WriteLog.SetupSequence(x => x.TryBeginAsync(f.RegisterId, It.IsAny<Guid>(),
                ReferenceRegisterIndependentWriteOperation.Tombstone, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostingStateBeginResult.AlreadyCompleted)
            .ReturnsAsync(PostingStateBeginResult.InProgress)
            .ReturnsAsync(PostingStateBeginResult.Begun)
            .ReturnsAsync(PostingStateBeginResult.Begun)
            .ReturnsAsync(PostingStateBeginResult.Begun);

        (await f.Sut.TombstoneAsync(f.RegisterId, [], Now, Guid.NewGuid()))
            .Should().Be(ReferenceRegisterWriteResult.AlreadyCompleted);
        await ((Func<Task>)(() => f.Sut.TombstoneByDimensionSetIdAsync(f.RegisterId, setId, Now, Guid.NewGuid())))
            .Should().ThrowAsync<ReferenceRegisterIndependentWriteAlreadyInProgressException>();

        f.Reader.SetupSequence(x => x.SliceLastForEffectiveMomentAsync(f.RegisterId, setId,
                Now, Now, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReferenceRegisterRecordRead?)null)
            .ReturnsAsync(Record(setId, null, deleted: true))
            .ReturnsAsync(Record(setId, null, deleted: false));
        (await f.Sut.TombstoneByDimensionSetIdAsync(f.RegisterId, setId, Now, Guid.NewGuid()))
            .Should().Be(ReferenceRegisterWriteResult.Executed);
        (await f.Sut.TombstoneByDimensionSetIdAsync(f.RegisterId, setId, Now, Guid.NewGuid(), manageTransaction: false))
            .Should().Be(ReferenceRegisterWriteResult.Executed);
        (await f.Sut.TombstoneByDimensionSetIdAsync(f.RegisterId, setId, Now, Guid.NewGuid()))
            .Should().Be(ReferenceRegisterWriteResult.Executed);

        f.Store.Verify(x => x.AppendAsync(f.RegisterId,
            It.Is<IReadOnlyList<ReferenceRegisterRecordWrite>>(r => r.Count == 1 && r[0].IsDeleted),
            It.IsAny<CancellationToken>()), Times.Once);
        f.Audit.Verify(x => x.WriteAsync(AuditEntityKind.ReferenceRegister, f.RegisterId,
            AuditActionCodes.ReferenceRegisterRecordsTombstone, It.IsAny<IReadOnlyList<AuditFieldChange>?>(),
            It.IsAny<object?>(), null, It.IsAny<CancellationToken>()), Times.Once);
        f.WriteLog.Verify(x => x.MarkCompletedAsync(f.RegisterId, It.IsAny<Guid>(),
            ReferenceRegisterIndependentWriteOperation.Tombstone, Now, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    private static async Task AssertValidation(Task task, string reason)
    {
        var assertion = await ((Func<Task>)(async () => await task)).Should()
            .ThrowAsync<ReferenceRegisterRecordsValidationException>();
        assertion.Which.Reason.Should().Be(reason);
    }

    private static void CaptureAudit(Mock<IAuditLogService> audit, Action<IReadOnlyList<AuditFieldChange>?> capture)
        => audit.Setup(x => x.WriteAsync(It.IsAny<AuditEntityKind>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<AuditFieldChange>?>(), It.IsAny<object?>(), null, It.IsAny<CancellationToken>()))
            .Callback<AuditEntityKind, Guid, string, IReadOnlyList<AuditFieldChange>?, object?, Guid?, CancellationToken>(
                (_, _, _, changes, _, _, _) => capture(changes))
            .Returns(Task.CompletedTask);

    private sealed class Fixture
    {
        public Guid RegisterId { get; }
        public Mock<IUnitOfWork> Uow { get; } = new(MockBehavior.Loose);
        public Mock<IReferenceRegisterRepository> Registers { get; } = new(MockBehavior.Loose);
        public Mock<IReferenceRegisterIndependentWriteStateRepository> WriteLog { get; } = new(MockBehavior.Loose);
        public Mock<IReferenceRegisterKeyLock> KeyLock { get; } = new(MockBehavior.Loose);
        public Mock<IReferenceRegisterRecordsStore> Store { get; } = new(MockBehavior.Loose);
        public Mock<IReferenceRegisterRecordsReader> Reader { get; } = new(MockBehavior.Loose);
        public Mock<IReferenceRegisterDimensionRuleRepository> Rules { get; } = new(MockBehavior.Loose);
        public Mock<IDimensionSetReader> Bags { get; } = new(MockBehavior.Loose);
        public Mock<IDimensionSetService> DimensionSets { get; } = new(MockBehavior.Loose);
        public Mock<IAuditLogService> Audit { get; } = new(MockBehavior.Loose);
        public ReferenceRegisterIndependentWriteService Sut { get; }

        public Fixture(ReferenceRegisterAdminItem? register = null, bool missing = false)
        {
            RegisterId = register?.RegisterId ?? Guid.NewGuid();
            if (!missing)
            {
                var resolved = register ?? Register(RegisterId, ReferenceRegisterPeriodicity.NonPeriodic);
                Registers.Setup(x => x.GetByIdAsync(RegisterId, It.IsAny<CancellationToken>())).ReturnsAsync(resolved);
            }
            Rules.Setup(x => x.GetByRegisterIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Bags.Setup(x => x.GetBagsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, DimensionBag>());
            WriteLog.Setup(x => x.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<ReferenceRegisterIndependentWriteOperation>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(PostingStateBeginResult.Begun);
            Sut = new(Uow.Object, Registers.Object, WriteLog.Object, KeyLock.Object, Store.Object,
                Reader.Object, Rules.Object, Bags.Object, DimensionSets.Object, Audit.Object,
                NullLogger<ReferenceRegisterIndependentWriteService>.Instance, new FixedTimeProvider(Now));
        }
    }

    private static ReferenceRegisterAdminItem Register(Guid id, ReferenceRegisterPeriodicity periodicity,
        ReferenceRegisterRecordMode mode = ReferenceRegisterRecordMode.Independent)
        => new(id, "prices", "prices", "prices", "Prices", periodicity, mode, false, Now, Now);

    private static ReferenceRegisterRecordRead Record(Guid setId, DateTime? period, bool deleted)
        => new(1, setId, period, period, null, Now, deleted,
            new Dictionary<string, object?> { ["amount"] = 5m });

    private static DimensionBag Bag(Guid dimension)
        => new([new DimensionValue(dimension, Guid.NewGuid())]);

    private static DateTime LocalTime() => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
