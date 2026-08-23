using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Accounting.Posting;
using NGB.Accounting.PostingState;
using NGB.Accounting.Registers;
using NGB.Core.AuditLog;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Numbering;
using NGB.Core.Documents.Exceptions;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Documents;
using NGB.Persistence.Locks;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.PostingState;
using NGB.Persistence.Readers;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Numbering;
using NGB.Runtime.Documents.Policies;
using NGB.Runtime.Documents.Posting;
using NGB.Runtime.Documents.Validation;
using NGB.Runtime.Documents.Workflow;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.Posting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Documents;

public sealed class DocumentPostingServiceFullCoverageTests
{
    [Fact]
    public async Task Explicit_post_and_repost_actions_are_required()
    {
        var sut = CreateSut();

        Func<Task> post = () => sut.PostAsync(Guid.NewGuid(), null!, CancellationToken.None);
        Func<Task> repost = () => sut.RepostAsync(Guid.NewGuid(), null!, CancellationToken.None);

        (await post.Should().ThrowAsync<NgbArgumentRequiredException>())
            .Which.ParamName.Should().Be("postingAction");
        (await repost.Should().ThrowAsync<NgbArgumentRequiredException>())
            .Which.ParamName.Should().Be("postNew");
    }

    [Fact]
    public async Task Recorder_tombstones_page_active_records_and_preserve_required_values()
    {
        var registerId = Guid.NewGuid();
        var recorderId = Guid.NewGuid();
        var deletedId = Guid.NewGuid();
        var firstActiveId = Guid.NewGuid();
        var secondActiveId = Guid.NewGuid();
        var period = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var asOf = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
        var reader = new Mock<IReferenceRegisterRecordsReader>(MockBehavior.Strict);
        var firstPage = new ReferenceRegisterRecordRead[]
        {
            Read(deletedId, recorderId, isDeleted: true, period, new Dictionary<string, object?> { ["value"] = 1 }),
            Read(firstActiveId, recorderId, isDeleted: false, period, new Dictionary<string, object?> { ["value"] = 2 })
        };
        var secondPage = new ReferenceRegisterRecordRead[]
        {
            Read(secondActiveId, recorderId, isDeleted: false, period, new Dictionary<string, object?> { ["value"] = "last" })
        };

        reader.Setup(x => x.SliceLastAllAsync(registerId, asOf, recorderId, null, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstPage);
        reader.Setup(x => x.SliceLastAllAsync(registerId, asOf, recorderId, firstActiveId, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(secondPage);
        reader.Setup(x => x.SliceLastAllAsync(registerId, asOf, recorderId, secondActiveId, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateSut(referenceRegisterRecordsReader: reader.Object)
            .BuildReferenceRegisterRecorderTombstonesAsync(
                registerId,
                recorderId,
                asOf,
                keepDimensionSetIds: null,
                CancellationToken.None);

        result.Should().HaveCount(2);
        result.Select(x => x.DimensionSetId).Should().Equal(firstActiveId, secondActiveId);
        result.Should().OnlyContain(x => x.IsDeleted && x.RecorderDocumentId == recorderId && x.PeriodUtc == period);
        result[0].Values.Should().Contain("value", 2);
        result[1].Values.Should().Contain("value", "last");
        result[0].Values.Should().NotBeSameAs(firstPage[1].Values);
        reader.VerifyAll();
    }

    [Fact]
    public async Task Recorder_tombstones_skip_kept_keys_for_list_and_hash_set_inputs()
    {
        var registerId = Guid.NewGuid();
        var recorderId = Guid.NewGuid();
        var keptId = Guid.NewGuid();
        var removedId = Guid.NewGuid();
        var asOf = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);

        foreach (IReadOnlyCollection<Guid> keep in new IReadOnlyCollection<Guid>[]
                 {
                     new[] { keptId },
                     new HashSet<Guid> { keptId }
                 })
        {
            var reader = new Mock<IReferenceRegisterRecordsReader>(MockBehavior.Strict);
            reader.Setup(x => x.SliceLastAllAsync(registerId, asOf, recorderId, null, 200, It.IsAny<CancellationToken>()))
                .ReturnsAsync([
                    Read(keptId, recorderId, isDeleted: false, period: null, new Dictionary<string, object?>()),
                    Read(removedId, recorderId, isDeleted: false, period: null, new Dictionary<string, object?>())
                ]);
            reader.Setup(x => x.SliceLastAllAsync(registerId, asOf, recorderId, removedId, 200, It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            var result = await CreateSut(referenceRegisterRecordsReader: reader.Object)
                .BuildReferenceRegisterRecorderTombstonesAsync(
                    registerId,
                    recorderId,
                    asOf,
                    keep,
                    CancellationToken.None);

            result.Should().ContainSingle().Which.DimensionSetId.Should().Be(removedId);
            reader.VerifyAll();
        }
    }

    [Fact]
    public async Task Accounting_entries_to_reverse_cover_empty_history_absent_empty_and_nonempty_handlers()
    {
        var document = new DocumentRecord
        {
            Id = Guid.NewGuid(),
            TypeCode = "test.document",
            DateUtc = DateTime.UnixEpoch,
            Status = DocumentStatus.Posted
        };
        var historical = new[] { Entry(document.Id, 1m) };
        var replacement = new[] { Entry(document.Id, 2m) };
        var resolver = new Mock<IDocumentPostingActionResolver>(MockBehavior.Strict);
        var factory = new Mock<IAccountingPostingContextFactory>(MockBehavior.Strict);

        var noHistory = await CreateSut(postingActionResolver: resolver.Object, accountingContextFactory: factory.Object)
            .GetAccountingEntriesToReverseAsync(document, [], "Document.Unpost", CancellationToken.None);
        noHistory.Should().BeEmpty();

        resolver.Setup(x => x.TryResolve(document)).Returns((Func<IAccountingPostingContext, CancellationToken, Task>?)null);
        var withoutHandler = await CreateSut(postingActionResolver: resolver.Object, accountingContextFactory: factory.Object)
            .GetAccountingEntriesToReverseAsync(document, historical, "Document.Unpost", CancellationToken.None);
        withoutHandler.Should().BeSameAs(historical);

        var emptyContext = new Mock<IAccountingPostingContext>(MockBehavior.Strict);
        emptyContext.SetupGet(x => x.Entries).Returns([]);
        resolver.Setup(x => x.TryResolve(document)).Returns((_, _) => Task.CompletedTask);
        factory.Setup(x => x.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(emptyContext.Object);
        Func<Task> emptySnapshot = () => CreateSut(postingActionResolver: resolver.Object, accountingContextFactory: factory.Object)
            .GetAccountingEntriesToReverseAsync(document, historical, "Document.Repost", CancellationToken.None);
        var exception = await emptySnapshot.Should().ThrowAsync<NgbInvariantViolationException>();
        exception.Which.Context.Should().Contain("documentId", document.Id)
            .And.Contain("typeCode", document.TypeCode)
            .And.Contain("operation", "Document.Repost");

        var replacementContext = new Mock<IAccountingPostingContext>(MockBehavior.Strict);
        replacementContext.SetupGet(x => x.Entries).Returns(replacement);
        factory.Setup(x => x.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(replacementContext.Object);
        var withHandler = await CreateSut(postingActionResolver: resolver.Object, accountingContextFactory: factory.Object)
            .GetAccountingEntriesToReverseAsync(document, historical, "Document.Unpost", CancellationToken.None);
        withHandler.Should().Equal(replacement);
    }

    [Fact]
    public async Task Public_workflow_guards_reject_accounting_history_mismatches_and_unknown_statuses()
    {
        var documentId = Guid.NewGuid();
        var posted = Document(documentId, DocumentStatus.Posted);
        var unknown = Document(documentId, (DocumentStatus)short.MaxValue);
        var uow = TransactionalUow();
        var locks = new Mock<IAdvisoryLockManager>();
        locks.Setup(x => x.LockDocumentAsync(documentId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var documents = new Mock<IDocumentRepository>();
        var entries = new Mock<IAccountingEntryReader>();
        entries.Setup(x => x.GetByDocumentAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var resolver = new Mock<IDocumentPostingActionResolver>();
        resolver.Setup(x => x.TryResolve(posted))
            .Returns((_, _) => Task.CompletedTask);
        documents.Setup(x => x.GetForUpdateAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(posted);
        var sut = CreateSut(
            uow: uow.Object,
            advisoryLocks: locks.Object,
            documents: documents.Object,
            entryReader: entries.Object,
            postingActionResolver: resolver.Object);

        Func<Task> unpostWithoutHistory = () => sut.UnpostAsync(documentId, CancellationToken.None);
        Func<Task> repostWithoutHistory = () => sut.RepostAsync(
            documentId,
            (_, _) => Task.CompletedTask,
            CancellationToken.None);
        (await unpostWithoutHistory.Should().ThrowAsync<NgbInvariantViolationException>())
            .Which.Context.Should().Contain("operation", "Document.Unpost");
        (await repostWithoutHistory.Should().ThrowAsync<NgbInvariantViolationException>())
            .Which.Context.Should().Contain("operation", "Document.Repost");

        documents.Setup(x => x.GetForUpdateAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(unknown);
        Func<Task> markUnknown = () => sut.MarkForDeletionAsync(documentId, CancellationToken.None);
        Func<Task> unmarkUnknown = () => sut.UnmarkForDeletionAsync(documentId, CancellationToken.None);
        await markUnknown.Should().ThrowAsync<DocumentWorkflowStateMismatchException>();
        await unmarkUnknown.Should().ThrowAsync<DocumentWorkflowStateMismatchException>();
    }

    [Fact]
    public async Task Public_workflows_report_missing_documents_at_each_lookup_boundary()
    {
        var documentId = Guid.NewGuid();
        var documents = new Mock<IDocumentRepository>();
        documents.Setup(x => x.GetForUpdateAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null);
        documents.Setup(x => x.GetAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null);
        var locks = new Mock<IAdvisoryLockManager>();
        locks.Setup(x => x.LockDocumentAsync(documentId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var sut = CreateSut(
            uow: TransactionalUow().Object,
            advisoryLocks: locks.Object,
            documents: documents.Object);

        var operations = new Func<Task>[]
        {
            () => sut.PostAsync(documentId, (_, _) => Task.CompletedTask),
            () => sut.UnpostAsync(documentId),
            () => sut.RepostAsync(documentId, (_, _) => Task.CompletedTask),
            () => sut.MarkForDeletionAsync(documentId),
            () => sut.UnmarkForDeletionAsync(documentId),
            () => sut.RepostAsync(documentId, manageTransaction: false)
        };

        foreach (var operation in operations)
            await operation.Should().ThrowAsync<DocumentNotFoundException>();
    }

    [Fact]
    public async Task Post_reports_deleted_timestamp_and_missing_numbering_reread()
    {
        var documentId = Guid.NewGuid();
        var deletedAt = new DateTime(2026, 8, 2, 3, 4, 5, DateTimeKind.Utc);
        var deleted = Document(documentId, DocumentStatus.MarkedForDeletion, deletedAt);
        var documents = new Mock<IDocumentRepository>();
        documents.Setup(x => x.GetForUpdateAsync(documentId, It.IsAny<CancellationToken>())).ReturnsAsync(deleted);
        var locks = new Mock<IAdvisoryLockManager>();
        locks.Setup(x => x.LockDocumentAsync(documentId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var deletedSut = CreateSut(
            uow: TransactionalUow().Object,
            advisoryLocks: locks.Object,
            documents: documents.Object);

        var deletedPost = () => deletedSut.PostAsync(documentId);
        (await deletedPost.Should().ThrowAsync<DocumentMarkedForDeletionException>())
            .Which.Context.Should().ContainValue(deletedAt);

        var draft = Document(documentId, DocumentStatus.Draft);
        documents.SetupSequence(x => x.GetForUpdateAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft)
            .ReturnsAsync((DocumentRecord?)null);
        var policy = new Mock<IDocumentNumberingPolicy>();
        policy.SetupGet(x => x.EnsureNumberOnPost).Returns(true);
        var policies = new Mock<IDocumentNumberingPolicyResolver>();
        policies.Setup(x => x.Resolve(draft.TypeCode)).Returns(policy.Object);
        var numbering = new Mock<IDocumentNumberingAndTypedSyncService>();
        numbering.Setup(x => x.EnsureNumberAndSyncTypedAsync(draft, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("N-1");
        var numberingSut = CreateSut(
            uow: TransactionalUow().Object,
            advisoryLocks: locks.Object,
            documents: documents.Object,
            numberingSync: numbering.Object,
            numberingPolicies: policies.Object);

        var missingReread = () => numberingSut.PostAsync(documentId);
        await missingReread.Should().ThrowAsync<DocumentNotFoundException>();
    }

    [Fact]
    public async Task Mark_and_unmark_report_a_missing_post_update_reread()
    {
        var documentId = Guid.NewGuid();
        var locks = new Mock<IAdvisoryLockManager>();
        locks.Setup(x => x.LockDocumentAsync(documentId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        foreach (var initial in new[]
                 {
                     Document(documentId, DocumentStatus.Draft),
                     Document(documentId, DocumentStatus.MarkedForDeletion)
                 })
        {
            var documents = new Mock<IDocumentRepository>();
            documents.SetupSequence(x => x.GetForUpdateAsync(documentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(initial)
                .ReturnsAsync((DocumentRecord?)null);
            documents.Setup(x => x.UpdateStatusAsync(
                    documentId,
                    It.IsAny<DocumentStatus>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<DateTime?>(),
                    It.IsAny<DateTime?>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var sut = CreateSut(
                uow: TransactionalUow().Object,
                advisoryLocks: locks.Object,
                documents: documents.Object);

            Func<Task> operation = initial.Status == DocumentStatus.Draft
                ? () => sut.MarkForDeletionAsync(documentId)
                : () => sut.UnmarkForDeletionAsync(documentId);
            await operation.Should().ThrowAsync<DocumentNotFoundException>();
        }
    }

    [Theory]
    [InlineData(PostingStateBeginResult.AlreadyCompleted)]
    [InlineData(PostingStateBeginResult.InProgress)]
    public async Task Reference_register_post_rejects_duplicate_and_concurrent_writes(PostingStateBeginResult begin)
    {
        const string registerCode = "coverage.reference";
        var registerId = ReferenceRegisterId.FromCode(registerCode);
        var documentId = Guid.NewGuid();
        var draft = Document(documentId, DocumentStatus.Draft);
        var refregAction = new Mock<IDocumentReferenceRegisterPostingActionResolver>();
        refregAction.Setup(x => x.TryResolve(draft)).Returns((builder, _, _) =>
        {
            builder.Add(registerCode, RefRecord(documentId));
            return Task.CompletedTask;
        });
        var state = new Mock<IReferenceRegisterWriteStateRepository>();
        state.Setup(x => x.TryBeginAsync(
                registerId,
                documentId,
                ReferenceRegisterWriteOperation.Post,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(begin);
        var sut = WorkflowSut(draft, refregAction: refregAction.Object, refregState: state.Object);

        var act = () => sut.PostAsync(documentId);
        var error = await Xunit.Record.ExceptionAsync(act);

        if (begin == PostingStateBeginResult.AlreadyCompleted)
            error.Should().BeOfType<NgbInvariantViolationException>();
        else
            error.Should().BeOfType<ReferenceRegisterWriteAlreadyInProgressException>();
    }

    [Theory]
    [InlineData(PostingStateBeginResult.AlreadyCompleted)]
    [InlineData(PostingStateBeginResult.InProgress)]
    public async Task Reference_register_unpost_rejects_duplicate_and_concurrent_writes(PostingStateBeginResult begin)
    {
        var registerId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var posted = Document(documentId, DocumentStatus.Posted);
        var state = new Mock<IReferenceRegisterWriteStateRepository>();
        state.Setup(x => x.GetRegisterIdsByDocumentAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([registerId]);
        state.Setup(x => x.TryBeginAsync(
                registerId,
                documentId,
                ReferenceRegisterWriteOperation.Unpost,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(begin);
        var sut = WorkflowSut(posted, refregState: state.Object);

        var error = await Xunit.Record.ExceptionAsync(() => sut.UnpostAsync(documentId));

        if (begin == PostingStateBeginResult.AlreadyCompleted)
            error.Should().BeOfType<NgbInvariantViolationException>();
        else
            error.Should().BeOfType<ReferenceRegisterWriteAlreadyInProgressException>();
    }

    [Fact]
    public async Task Reference_register_unpost_fallback_tombstones_active_records_and_reports_missing_metadata()
    {
        var registerId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var posted = Document(documentId, DocumentStatus.Posted);
        var state = RefregState(registerId, documentId, ReferenceRegisterWriteOperation.Unpost);
        var metadata = new Mock<IReferenceRegisterRepository>();
        metadata.Setup(x => x.GetByIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReferenceRegisterAdminItem?)null);
        var missingSut = WorkflowSut(posted, refregState: state.Object, refregRepository: metadata.Object);
        Func<Task> missingMetadata = () => missingSut.UnpostAsync(documentId);
        await missingMetadata.Should().ThrowAsync<ReferenceRegisterNotFoundException>();

        metadata.Setup(x => x.GetByIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register(registerId, ReferenceRegisterPeriodicity.NonPeriodic));
        var dimensionSetId = Guid.NewGuid();
        var reader = new Mock<IReferenceRegisterRecordsReader>();
        reader.SetupSequence(x => x.SliceLastAllAsync(
                registerId,
                It.IsAny<DateTime>(),
                documentId,
                It.IsAny<Guid?>(),
                200,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([Read(dimensionSetId, documentId, false, null, new Dictionary<string, object?> { ["required"] = 1 })])
            .ReturnsAsync([]);
        var store = new Mock<IReferenceRegisterRecordsStore>();
        store.Setup(x => x.AppendAsync(registerId, It.IsAny<IReadOnlyList<ReferenceRegisterRecordWrite>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var sut = WorkflowSut(
            posted,
            refregState: RefregState(registerId, documentId, ReferenceRegisterWriteOperation.Unpost).Object,
            refregRepository: metadata.Object,
            refregReader: reader.Object,
            refregStore: store.Object);

        await sut.UnpostAsync(documentId);

        store.Verify(x => x.AppendAsync(
            registerId,
            It.Is<IReadOnlyList<ReferenceRegisterRecordWrite>>(records =>
                records.Count == 1 && records[0].IsDeleted && records[0].DimensionSetId == dimensionSetId),
            It.IsAny<CancellationToken>()));
    }

    [Theory]
    [InlineData(PostingStateBeginResult.AlreadyCompleted)]
    [InlineData(PostingStateBeginResult.InProgress)]
    public async Task Register_only_repost_rejects_operational_and_reference_register_state_conflicts(
        PostingStateBeginResult begin)
    {
        var registerId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var posted = Document(documentId, DocumentStatus.Posted);

        var opregState = new Mock<IOperationalRegisterWriteStateRepository>();
        opregState.Setup(x => x.GetRegisterIdsByDocumentAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([registerId]);
        var applier = new Mock<IOperationalRegisterMovementsApplier>();
        applier.Setup(x => x.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Repost,
                It.IsAny<IReadOnlyList<OperationalRegisterMovement>>(),
                null,
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationalRegisterWriteResult.AlreadyCompleted);
        var opregSut = WorkflowSut(posted, opregState: opregState.Object, opregApplier: applier.Object);
        Func<Task> opregConflict = () => opregSut.RepostAsync(documentId, (_, _) => Task.CompletedTask);
        await opregConflict.Should().ThrowAsync<NgbInvariantViolationException>();

        var refregState = new Mock<IReferenceRegisterWriteStateRepository>();
        refregState.Setup(x => x.GetRegisterIdsByDocumentAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([registerId]);
        refregState.Setup(x => x.TryBeginAsync(
                registerId,
                documentId,
                ReferenceRegisterWriteOperation.Repost,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(begin);
        var refregSut = WorkflowSut(posted, refregState: refregState.Object);
        var error = await Xunit.Record.ExceptionAsync(() => refregSut.RepostAsync(documentId, (_, _) => Task.CompletedTask));

        if (begin == PostingStateBeginResult.AlreadyCompleted)
            error.Should().BeOfType<NgbInvariantViolationException>();
        else
            error.Should().BeOfType<ReferenceRegisterWriteAlreadyInProgressException>();
    }

    [Fact]
    public async Task Register_only_repost_fallback_tombstones_removed_keys_for_empty_and_nonempty_replacements()
    {
        const string registerCode = "coverage.repost.reference";
        var registerId = ReferenceRegisterId.FromCode(registerCode);
        var documentId = Guid.NewGuid();
        var posted = Document(documentId, DocumentStatus.Posted);

        foreach (var emitReplacement in new[] { false, true })
        {
            var state = RefregState(registerId, documentId, ReferenceRegisterWriteOperation.Repost);
            var metadata = new Mock<IReferenceRegisterRepository>();
            metadata.Setup(x => x.GetByIdAsync(registerId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Register(registerId, ReferenceRegisterPeriodicity.NonPeriodic));
            var removedId = Guid.NewGuid();
            var reader = new Mock<IReferenceRegisterRecordsReader>();
            reader.SetupSequence(x => x.SliceLastAllAsync(
                    registerId,
                    It.IsAny<DateTime>(),
                    documentId,
                    It.IsAny<Guid?>(),
                    200,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([Read(removedId, documentId, false, null, new Dictionary<string, object?>())])
                .ReturnsAsync([]);
            var store = new Mock<IReferenceRegisterRecordsStore>();
            store.Setup(x => x.AppendAsync(registerId, It.IsAny<IReadOnlyList<ReferenceRegisterRecordWrite>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var action = new Mock<IDocumentReferenceRegisterPostingActionResolver>();
            if (emitReplacement)
            {
                action.Setup(x => x.TryResolve(posted)).Returns((builder, _, _) =>
                {
                    builder.Add(registerCode, RefRecord(documentId));
                    return Task.CompletedTask;
                });
            }

            var sut = WorkflowSut(
                posted,
                refregAction: action.Object,
                refregState: state.Object,
                refregRepository: metadata.Object,
                refregReader: reader.Object,
                refregStore: store.Object);

            await sut.RepostAsync(documentId, (_, _) => Task.CompletedTask);

            store.Verify(x => x.AppendAsync(
                registerId,
                It.Is<IReadOnlyList<ReferenceRegisterRecordWrite>>(records => records.Count == 1 && records[0].IsDeleted),
                It.IsAny<CancellationToken>()));
        }
    }

    [Fact]
    public async Task Shared_workflow_guards_cover_success_failure_and_boundary_values()
    {
        var documentId = Guid.NewGuid();
        var document = Document(documentId, DocumentStatus.Draft);
        DocumentPostingService.RequireDocument(document, documentId).Should().BeSameAs(document);
        Action missingDocument = () => DocumentPostingService.RequireDocument(null, documentId);
        missingDocument.Should().Throw<DocumentNotFoundException>();

        var registerId = Guid.NewGuid();
        var register = Register(registerId, ReferenceRegisterPeriodicity.NonPeriodic);
        DocumentPostingService.RequireReferenceRegister(register, registerId).Should().BeSameAs(register);
        Action missingRegister = () => DocumentPostingService.RequireReferenceRegister(null, registerId);
        missingRegister.Should().Throw<ReferenceRegisterNotFoundException>();

        var markedAt = new DateTime(2026, 8, 3, 4, 5, 6, DateTimeKind.Utc);
        DocumentPostingService.ResolveDeletionMarkTimestamp(Document(documentId, DocumentStatus.Draft, markedAt))
            .Should().Be(markedAt);
        DocumentPostingService.ResolveDeletionMarkTimestamp(document).Should().Be(document.UpdatedAtUtc);

        var changes = new List<AuditFieldChange>();
        DocumentPostingService.AddClearedDeletionMarkChange(changes, null);
        DocumentPostingService.AddClearedDeletionMarkChange(changes, markedAt);
        changes.Should().ContainSingle(change => change.FieldPath == "marked_for_deletion_at_utc");

        var context = Mock.Of<IAccountingPostingContext>();
        var invoked = false;
        await DocumentPostingService.InvokeResolvedPostingActionAsync(
            (_, _) =>
            {
                invoked = true;
                return Task.CompletedTask;
            },
            context,
            CancellationToken.None,
            documentId,
            document.TypeCode);
        invoked.Should().BeTrue();
        Func<Task> missingAction = () => DocumentPostingService.InvokeResolvedPostingActionAsync(
            null,
            context,
            CancellationToken.None,
            documentId,
            document.TypeCode);
        await missingAction.Should().ThrowAsync<DocumentPostingHandlerNotConfiguredException>();

        DocumentPostingService.EnsureReferenceRegisterWriteBegun(
            PostingStateBeginResult.Begun,
            registerId,
            documentId,
            ReferenceRegisterWriteOperation.Post);
        Action completedReferenceWrite = () => DocumentPostingService.EnsureReferenceRegisterWriteBegun(
            PostingStateBeginResult.AlreadyCompleted,
            registerId,
            documentId,
            ReferenceRegisterWriteOperation.Post);
        Action concurrentReferenceWrite = () => DocumentPostingService.EnsureReferenceRegisterWriteBegun(
            PostingStateBeginResult.InProgress,
            registerId,
            documentId,
            ReferenceRegisterWriteOperation.Post);
        completedReferenceWrite.Should().Throw<NgbInvariantViolationException>();
        concurrentReferenceWrite.Should().Throw<ReferenceRegisterWriteAlreadyInProgressException>();

        DocumentPostingService.EnsureOperationalRegisterExecuted(
            OperationalRegisterWriteResult.Executed,
            registerId,
            documentId,
            OperationalRegisterWriteOperation.Repost);
        Action completedOperationalWrite = () => DocumentPostingService.EnsureOperationalRegisterExecuted(
            OperationalRegisterWriteResult.AlreadyCompleted,
            registerId,
            documentId,
            OperationalRegisterWriteOperation.Repost);
        completedOperationalWrite.Should().Throw<NgbInvariantViolationException>();
    }

    [Fact]
    public async Task Unpost_uses_optimized_recorder_tombstone_writer_when_available()
    {
        var registerId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var posted = Document(documentId, DocumentStatus.Posted);
        var metadata = new Mock<IReferenceRegisterRepository>();
        metadata.Setup(x => x.GetByIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register(registerId, ReferenceRegisterPeriodicity.NonPeriodic));
        var store = new Mock<IReferenceRegisterRecordsStore>();
        var writer = store.As<IReferenceRegisterRecorderTombstoneWriter>();
        writer.Setup(x => x.AppendTombstonesForRecorderAsync(
                registerId,
                documentId,
                null,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var sut = WorkflowSut(
            posted,
            refregState: RefregState(registerId, documentId, ReferenceRegisterWriteOperation.Unpost).Object,
            refregRepository: metadata.Object,
            refregStore: store.Object);

        await sut.UnpostAsync(documentId);

        writer.VerifyAll();
    }

    private static ReferenceRegisterRecordRead Read(
        Guid dimensionSetId,
        Guid recorderId,
        bool isDeleted,
        DateTime? period,
        IReadOnlyDictionary<string, object?> values)
        => new(
            RecordId: 1,
            DimensionSetId: dimensionSetId,
            PeriodUtc: period,
            PeriodBucketUtc: period,
            RecorderDocumentId: recorderId,
            RecordedAtUtc: DateTime.UnixEpoch,
            IsDeleted: isDeleted,
            Values: values);

    private static AccountingEntry Entry(Guid documentId, decimal amount)
        => new()
        {
            DocumentId = documentId,
            Period = DateTime.UnixEpoch,
            Debit = null!,
            Credit = null!,
            Amount = amount
        };

    private static ReferenceRegisterRecordWrite RefRecord(Guid documentId)
        => new(Guid.NewGuid(), null, documentId, new Dictionary<string, object?>());

    private static ReferenceRegisterAdminItem Register(Guid registerId, ReferenceRegisterPeriodicity periodicity)
        => new(
            registerId,
            "coverage.reference",
            "coverage.reference",
            "coverage_reference",
            "Coverage reference",
            periodicity,
            ReferenceRegisterRecordMode.SubordinateToRecorder,
            true,
            DateTime.UnixEpoch,
            DateTime.UnixEpoch);

    private static DocumentRecord Document(Guid id, DocumentStatus status, DateTime? markedForDeletionAtUtc = null)
        => new()
        {
            Id = id,
            TypeCode = "test.document",
            DateUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            Status = status,
            MarkedForDeletionAtUtc = markedForDeletionAtUtc,
            CreatedAtUtc = DateTime.UnixEpoch,
            UpdatedAtUtc = DateTime.UnixEpoch
        };

    private static Mock<IUnitOfWork> TransactionalUow()
    {
        var active = false;
        var uow = new Mock<IUnitOfWork>();
        uow.SetupGet(x => x.HasActiveTransaction).Returns(() => active);
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Callback(() => active = true)
            .Returns(Task.CompletedTask);
        uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
            .Callback(() => active = false)
            .Returns(Task.CompletedTask);
        uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>()))
            .Callback(() => active = false)
            .Returns(Task.CompletedTask);
        return uow;
    }

    private static Mock<IReferenceRegisterWriteStateRepository> RefregState(
        Guid registerId,
        Guid documentId,
        ReferenceRegisterWriteOperation operation)
    {
        var state = new Mock<IReferenceRegisterWriteStateRepository>();
        state.Setup(x => x.GetRegisterIdsByDocumentAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([registerId]);
        state.Setup(x => x.TryBeginAsync(
                registerId,
                documentId,
                operation,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostingStateBeginResult.Begun);
        state.Setup(x => x.MarkCompletedAsync(
                registerId,
                documentId,
                operation,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return state;
    }

    private static DocumentPostingService WorkflowSut(
        DocumentRecord document,
        IDocumentReferenceRegisterPostingActionResolver? refregAction = null,
        IReferenceRegisterWriteStateRepository? refregState = null,
        IReferenceRegisterRepository? refregRepository = null,
        IReferenceRegisterRecordsReader? refregReader = null,
        IReferenceRegisterRecordsStore? refregStore = null,
        IOperationalRegisterWriteStateRepository? opregState = null,
        IOperationalRegisterMovementsApplier? opregApplier = null)
    {
        var documents = new Mock<IDocumentRepository>();
        documents.Setup(x => x.GetForUpdateAsync(document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        documents.Setup(x => x.UpdateStatusAsync(
                document.Id,
                It.IsAny<DocumentStatus>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var locks = new Mock<IAdvisoryLockManager>();
        locks.Setup(x => x.LockDocumentAsync(document.Id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var entries = new Mock<IAccountingEntryReader>();
        entries.Setup(x => x.GetByDocumentAsync(document.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var validators = new Mock<IDocumentValidatorResolver>();
        validators.Setup(x => x.ResolvePostValidators(document.TypeCode)).Returns([]);
        var opregWriteState = opregState ?? Mock.Of<IOperationalRegisterWriteStateRepository>(x =>
            x.GetRegisterIdsByDocumentAsync(document.Id, It.IsAny<CancellationToken>()) ==
            Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>()));
        var refregWriteState = refregState ?? Mock.Of<IReferenceRegisterWriteStateRepository>(x =>
            x.GetRegisterIdsByDocumentAsync(document.Id, It.IsAny<CancellationToken>()) ==
            Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>()));

        return CreateSut(
            uow: TransactionalUow().Object,
            advisoryLocks: locks.Object,
            documents: documents.Object,
            entryReader: entries.Object,
            lifecycleCoordinator: BegunLifecycle(document.Id),
            opregWriteStateRepository: opregWriteState,
            opregMovementsApplier: opregApplier,
            refregPostingActionResolver: refregAction,
            refregWriteStateRepository: refregWriteState,
            refregRepository: refregRepository,
            referenceRegisterRecordsReader: refregReader,
            refregRecordsStore: refregStore,
            validators: validators.Object);
    }

    private static DocumentPostingLifecycleCoordinator BegunLifecycle(Guid documentId)
    {
        var documentState = new Mock<IDocumentOperationStateRepository>();
        documentState.Setup(x => x.TryBeginAsync(
                documentId,
                It.IsAny<PostingOperation>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostingStateBeginResult.Begun);
        documentState.Setup(x => x.MarkCompletedAsync(
                documentId,
                It.IsAny<PostingOperation>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        documentState.Setup(x => x.ClearCompletedStateAsync(
                documentId,
                It.IsAny<PostingOperation>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        documentState.Setup(x => x.ClearInProgressStateAsync(
                documentId,
                It.IsAny<PostingOperation>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var postingState = new Mock<IPostingStateRepository>();
        postingState.Setup(x => x.ClearCompletedStateAsync(
                documentId,
                It.IsAny<PostingOperation>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var opregState = new Mock<IOperationalRegisterWriteStateRepository>();
        opregState.Setup(x => x.ClearCompletedStateByDocumentAsync(
                documentId,
                It.IsAny<OperationalRegisterWriteOperation>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var refregState = new Mock<IReferenceRegisterWriteStateRepository>();
        refregState.Setup(x => x.ClearCompletedStateByDocumentAsync(
                documentId,
                It.IsAny<ReferenceRegisterWriteOperation>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return new DocumentPostingLifecycleCoordinator(
            documentState.Object,
            postingState.Object,
            opregState.Object,
            refregState.Object);
    }

    private static DocumentPostingService CreateSut(
        IReferenceRegisterRecordsReader? referenceRegisterRecordsReader = null,
        IDocumentPostingActionResolver? postingActionResolver = null,
        IAccountingPostingContextFactory? accountingContextFactory = null,
        IUnitOfWork? uow = null,
        IAdvisoryLockManager? advisoryLocks = null,
        IDocumentRepository? documents = null,
        IAccountingEntryReader? entryReader = null,
        DocumentPostingLifecycleCoordinator? lifecycleCoordinator = null,
        IOperationalRegisterMovementsApplier? opregMovementsApplier = null,
        IOperationalRegisterWriteStateRepository? opregWriteStateRepository = null,
        IDocumentReferenceRegisterPostingActionResolver? refregPostingActionResolver = null,
        IReferenceRegisterRecordsStore? refregRecordsStore = null,
        IReferenceRegisterWriteStateRepository? refregWriteStateRepository = null,
        IReferenceRegisterRepository? refregRepository = null,
        IDocumentNumberingAndTypedSyncService? numberingSync = null,
        IDocumentNumberingPolicyResolver? numberingPolicies = null,
        IDocumentValidatorResolver? validators = null)
        => new(
            uow: uow ?? Mock.Of<IUnitOfWork>(),
            advisoryLocks: advisoryLocks ?? Mock.Of<IAdvisoryLockManager>(),
            documents: documents ?? Mock.Of<IDocumentRepository>(),
            postingEngine: null!,
            accountingContextFactory: accountingContextFactory ?? Mock.Of<IAccountingPostingContextFactory>(),
            entryReader: entryReader ?? Mock.Of<IAccountingEntryReader>(),
            lifecycleCoordinator: lifecycleCoordinator!,
            postingActionResolver: postingActionResolver ?? Mock.Of<IDocumentPostingActionResolver>(),
            opregPostingActionResolver: Mock.Of<IDocumentOperationalRegisterPostingActionResolver>(),
            opregMovementsApplier: opregMovementsApplier ?? Mock.Of<IOperationalRegisterMovementsApplier>(),
            opregWriteStateRepository: opregWriteStateRepository ?? Mock.Of<IOperationalRegisterWriteStateRepository>(),
            refregPostingActionResolver: refregPostingActionResolver ?? Mock.Of<IDocumentReferenceRegisterPostingActionResolver>(),
            refregRecordsStore: refregRecordsStore ?? Mock.Of<IReferenceRegisterRecordsStore>(),
            refregRecordsReader: referenceRegisterRecordsReader ?? Mock.Of<IReferenceRegisterRecordsReader>(),
            refregWriteStateRepository: refregWriteStateRepository ?? Mock.Of<IReferenceRegisterWriteStateRepository>(),
            refregRepository: refregRepository ?? Mock.Of<IReferenceRegisterRepository>(),
            validators: validators ?? Mock.Of<IDocumentValidatorResolver>(),
            writeEngine: null!,
            numberingSync: numberingSync ?? Mock.Of<IDocumentNumberingAndTypedSyncService>(),
            numberingPolicies: numberingPolicies ?? Mock.Of<IDocumentNumberingPolicyResolver>(),
            audit: Mock.Of<IAuditLogService>(),
            logger: NullLogger<DocumentPostingService>.Instance,
            timeProvider: TimeProvider.System);
}
