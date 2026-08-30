using FluentAssertions;
using Moq;
using NGB.Core.AuditLog;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Definitions.Documents.Numbering;
using NGB.Definitions.Documents.Validation;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.Storage;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Numbering;
using NGB.Runtime.Documents.Policies;
using NGB.Runtime.Documents.Validation;
using NGB.Runtime.Documents.Workflow;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Documents;

public sealed class DocumentDraftServiceFullCoverageTests
{
    private static readonly DateTime Now = new(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc);
    private static readonly DateTime Date = new(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Create_RejectsBlankNonUtcAndMissingTypeBeforeWriting()
    {
        var fixture = new Fixture();

        await ((Func<Task>)(() => fixture.Sut.CreateDraftAsync(" ", null, Date)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => fixture.Sut.CreateDraftAsync("doc", null,
                DateTime.SpecifyKind(Date, DateTimeKind.Local))))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        fixture.Types.Setup(x => x.TryGet("missing")).Returns((DocumentTypeMetadata?)null);
        await ((Func<Task>)(() => fixture.Sut.CreateDraftAsync("missing", null, Date)))
            .Should().ThrowAsync<DocumentTypeNotFoundException>();
        fixture.Documents.Verify(x => x.CreateAsync(It.IsAny<DocumentRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_NormalizesNumbersRunsValidatorsAuditAndSupportsExternalTransactionAndSuppression()
    {
        var fixture = new Fixture();
        var validator = new Mock<IDocumentDraftValidator>(MockBehavior.Strict);
        validator.SetupGet(x => x.TypeCode).Returns("doc");
        validator.Setup(x => x.ValidateCreateDraftAsync(
                It.Is<DocumentRecord>(d => d.TypeCode == "doc"),
                It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        fixture.Validators.Setup(x => x.ResolveDraftValidators("doc")).Returns([validator.Object]);
        var created = new List<DocumentRecord>();
        fixture.Documents.Setup(x => x.CreateAsync(It.IsAny<DocumentRecord>(), It.IsAny<CancellationToken>()))
            .Callback<DocumentRecord, CancellationToken>((record, _) => created.Add(record))
            .Returns(Task.CompletedTask);

        var audited = await fixture.Sut.CreateDraftAsync("doc", "  N-1  ", Date);
        fixture.Uow.SetupGet(x => x.HasActiveTransaction).Returns(true);
        fixture.Uow.Setup(x => x.EnsureActiveTransaction());
        var suppressed = await fixture.Sut.CreateDraftAsync(
            "doc", " ", Date, manageTransaction: false, suppressAudit: true);

        audited.Should().NotBeEmpty().And.NotBe(suppressed);
        created.Should().HaveCount(2);
        created[0].Should().Match<DocumentRecord>(d => d.Number == "N-1" && d.Status == DocumentStatus.Draft
            && d.DateUtc == Date && d.CreatedAtUtc == Now && d.UpdatedAtUtc == Now
            && d.PostedAtUtc == null && d.MarkedForDeletionAtUtc == null);
        created[1].Number.Should().BeNull();
        validator.Verify(x => x.ValidateCreateDraftAsync(It.IsAny<DocumentRecord>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        fixture.Audit.Verify(x => x.WriteAsync(
            AuditEntityKind.Document, audited, AuditActionCodes.DocumentCreateDraft,
            It.Is<IReadOnlyList<AuditFieldChange>>(changes => changes.Any(c => c.FieldPath == "number")),
            It.IsAny<object>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_NumberingPolicyCoversNullDisabledMissingLockedAndAssignedNumber()
    {
        var noPolicy = new Fixture();
        (await noPolicy.Sut.CreateDraftAsync("doc", null, Date)).Should().NotBeEmpty();

        var disabled = new Fixture();
        disabled.Policies.Setup(x => x.Resolve("doc")).Returns(Policy(ensureOnCreate: false));
        (await disabled.Sut.CreateDraftAsync("doc", null, Date)).Should().NotBeEmpty();

        var missing = new Fixture();
        missing.Policies.Setup(x => x.Resolve("doc")).Returns(Policy(ensureOnCreate: true));
        missing.Documents.Setup(x => x.GetForUpdateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null);
        await ((Func<Task>)(() => missing.Sut.CreateDraftAsync("doc", null, Date)))
            .Should().ThrowAsync<DocumentNotFoundException>();

        var assigned = new Fixture();
        assigned.Policies.Setup(x => x.Resolve("doc")).Returns(Policy(ensureOnCreate: true));
        assigned.Documents.Setup(x => x.GetForUpdateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => Record(id, number: null));
        assigned.Numbering.Setup(x => x.EnsureNumberAndSyncTypedAsync(
                It.IsAny<DocumentRecord>(), Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync("AUTO-1");

        var id = await assigned.Sut.CreateDraftAsync("doc", null, Date);

        assigned.Audit.Verify(x => x.WriteAsync(
            AuditEntityKind.Document, id, AuditActionCodes.DocumentCreateDraft,
            It.Is<IReadOnlyList<AuditFieldChange>>(changes => changes.Any(c => c.FieldPath == "number")),
            It.IsAny<object>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Batch_create_partitions_only_auto_numbered_items_and_preserves_request_order()
    {
        var fixture = new Fixture(batchRepository: true);
        fixture.Types.Setup(x => x.TryGet("numbered"))
            .Returns(new DocumentTypeMetadata("numbered", []));
        fixture.Policies.Setup(x => x.Resolve("numbered")).Returns(Policy(ensureOnCreate: true));
        var batchRecords = new List<DocumentRecord>();
        fixture.BatchDocuments!.Setup(x => x.CreateDraftsAsync(
                It.IsAny<IReadOnlyList<DocumentRecord>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<DocumentRecord>, CancellationToken>((records, _) => batchRecords.AddRange(records))
            .Returns(Task.CompletedTask);
        fixture.Documents.Setup(x => x.GetForUpdateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new DocumentRecord
            {
                Id = id,
                TypeCode = "numbered",
                DateUtc = Date,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = Now,
                UpdatedAtUtc = Now
            });
        fixture.Numbering.Setup(x => x.EnsureNumberAndSyncTypedAsync(
                It.Is<DocumentRecord>(record => record.TypeCode == "numbered"),
                Now,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("AUTO-1");

        var ids = await fixture.Sut.CreateDraftsAsync(
        [
            new DocumentDraftCreateRequest("doc", null, Date),
            new DocumentDraftCreateRequest("numbered", null, Date),
            new DocumentDraftCreateRequest("numbered", " MANUAL ", Date)
        ], suppressAudit: true);

        ids.Should().HaveCount(3).And.OnlyHaveUniqueItems();
        batchRecords.Select(record => record.Id).Should().Equal(ids[0], ids[2]);
        batchRecords.Select(record => record.Number).Should().Equal(null, "MANUAL");
        fixture.Documents.Verify(x => x.CreateAsync(
            It.Is<DocumentRecord>(record => record.Id == ids[1]),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Numbering.VerifyAll();
        fixture.BatchDocuments.Verify(x => x.CreateDraftsAsync(
            It.Is<IReadOnlyList<DocumentRecord>>(records => records.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Locks.Invocations
            .Where(invocation => invocation.Method.Name == nameof(IAdvisoryLockManager.LockDocumentAsync))
            .Select(invocation => (Guid)invocation.Arguments[0])
            .Should().Equal(ids.OrderBy(static id => id));
    }

    [Fact]
    public async Task Update_CoversInputNoopMissingMarkedFallbackAndWrongWorkflowState()
    {
        var fixture = new Fixture();
        var id = Guid.NewGuid();

        await ((Func<Task>)(() => fixture.Sut.UpdateDraftAsync(Guid.Empty, "x", null)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => fixture.Sut.UpdateDraftAsync(id, null,
                DateTime.SpecifyKind(Date, DateTimeKind.Unspecified))))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        (await fixture.Sut.UpdateDraftAsync(id, null, null)).Should().BeFalse();

        fixture.Documents.SetupSequence(x => x.GetForUpdateAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null)
            .ReturnsAsync(Record(id, status: DocumentStatus.MarkedForDeletion, markedAt: Now.AddDays(-1)))
            .ReturnsAsync(Record(id, status: DocumentStatus.MarkedForDeletion, markedAt: null))
            .ReturnsAsync(Record(id, status: DocumentStatus.Posted));
        await ((Func<Task>)(() => fixture.Sut.UpdateDraftAsync(id, "x", null)))
            .Should().ThrowAsync<DocumentNotFoundException>();
        (await ((Func<Task>)(() => fixture.Sut.UpdateDraftAsync(id, "x", null)))
            .Should().ThrowAsync<DocumentMarkedForDeletionException>()).Which.MarkedForDeletionAtUtc
            .Should().Be(Now.AddDays(-1));
        (await ((Func<Task>)(() => fixture.Sut.UpdateDraftAsync(id, "x", null)))
            .Should().ThrowAsync<DocumentMarkedForDeletionException>()).Which.MarkedForDeletionAtUtc
            .Should().Be(Now);
        await ((Func<Task>)(() => fixture.Sut.UpdateDraftAsync(id, "x", null)))
            .Should().ThrowAsync<DocumentWorkflowStateMismatchException>();
    }

    [Fact]
    public async Task Update_CoversUnchangedNumberDateOnlyNumberOnlyAndBothChangesWithValidatorAndAudit()
    {
        var id = Guid.NewGuid();
        var unchanged = new Fixture();
        unchanged.Documents.Setup(x => x.GetForUpdateAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Record(id, number: "N-1", date: Date));
        (await unchanged.Sut.UpdateDraftAsync(id, " N-1 ", Date)).Should().BeFalse();

        var cases = new[]
        {
            (Number: (string?)null, Date: (DateTime?)Date.AddDays(1), ExpectedNumber: "N-1"),
            (Number: (string?)"   ", Date: (DateTime?)null, ExpectedNumber: (string?)null),
            (Number: (string?)" N-2 ", Date: (DateTime?)Date.AddDays(2), ExpectedNumber: "N-2")
        };

        foreach (var test in cases)
        {
            var fixture = new Fixture();
            var validator = new Mock<IDocumentDraftValidator>(MockBehavior.Strict);
            validator.Setup(x => x.ValidateCreateDraftAsync(It.Is<DocumentRecord>(d => d.Number == test.ExpectedNumber),
                It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            fixture.Validators.Setup(x => x.ResolveDraftValidators("doc")).Returns([validator.Object]);
            fixture.Documents.Setup(x => x.GetForUpdateAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Record(id, number: "N-1", date: Date));
            fixture.Documents.Setup(x => x.UpdateDraftHeaderAsync(
                    id, test.ExpectedNumber, test.Date ?? Date, Now, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            (await fixture.Sut.UpdateDraftAsync(id, test.Number, test.Date)).Should().BeTrue();

            validator.Verify(x => x.ValidateCreateDraftAsync(It.IsAny<DocumentRecord>(), It.IsAny<CancellationToken>()), Times.Once);
            fixture.Audit.Verify(x => x.WriteAsync(
                AuditEntityKind.Document, id, AuditActionCodes.DocumentUpdateDraft,
                It.IsAny<IReadOnlyList<AuditFieldChange>>(), It.IsAny<object>(), null,
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task Delete_CoversEmptyMissingWrongStateStorageRaceBothAllowedStatesAndOptionalNumberAudit()
    {
        var fixture = new Fixture();
        var id = Guid.NewGuid();
        await ((Func<Task>)(() => fixture.Sut.DeleteDraftAsync(Guid.Empty)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();

        fixture.Documents.SetupSequence(x => x.GetForUpdateAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null)
            .ReturnsAsync(Record(id, status: DocumentStatus.Posted))
            .ReturnsAsync(Record(id, number: "N-1"))
            .ReturnsAsync(Record(id, number: null, status: DocumentStatus.MarkedForDeletion));
        fixture.Documents.SetupSequence(x => x.TryDeleteAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)
            .ReturnsAsync(true);

        (await fixture.Sut.DeleteDraftAsync(id)).Should().BeFalse();
        await ((Func<Task>)(() => fixture.Sut.DeleteDraftAsync(id)))
            .Should().ThrowAsync<DocumentWorkflowStateMismatchException>();
        (await fixture.Sut.DeleteDraftAsync(id)).Should().BeFalse();
        (await fixture.Sut.DeleteDraftAsync(id)).Should().BeTrue();

        fixture.Audit.Verify(x => x.WriteAsync(
            AuditEntityKind.Document, id, AuditActionCodes.DocumentDeleteDraft,
            It.Is<IReadOnlyList<AuditFieldChange>>(changes => changes.All(c => c.FieldPath != "number")),
            It.IsAny<object>(), null, It.IsAny<CancellationToken>()), Times.Once);

        var numbered = new Fixture();
        numbered.Documents.Setup(x => x.GetForUpdateAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Record(id, number: "N-2"));
        numbered.Documents.Setup(x => x.TryDeleteAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        (await numbered.Sut.DeleteDraftAsync(id)).Should().BeTrue();
        numbered.Audit.Verify(x => x.WriteAsync(
            AuditEntityKind.Document, id, AuditActionCodes.DocumentDeleteDraft,
            It.Is<IReadOnlyList<AuditFieldChange>>(changes => changes.Any(c => c.FieldPath == "number")),
            It.IsAny<object>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static IDocumentNumberingPolicy Policy(bool ensureOnCreate)
    {
        var policy = new Mock<IDocumentNumberingPolicy>(MockBehavior.Strict);
        policy.SetupGet(x => x.EnsureNumberOnCreateDraft).Returns(ensureOnCreate);
        return policy.Object;
    }

    private static DocumentRecord Record(
        Guid id,
        string? number = "N-1",
        DateTime? date = null,
        DocumentStatus status = DocumentStatus.Draft,
        DateTime? markedAt = null)
        => new()
        {
            Id = id,
            TypeCode = "doc",
            Number = number,
            DateUtc = date ?? Date,
            Status = status,
            CreatedAtUtc = Now.AddDays(-2),
            UpdatedAtUtc = Now.AddDays(-1),
            PostedAtUtc = status == DocumentStatus.Posted ? Now.AddDays(-1) : null,
            MarkedForDeletionAtUtc = markedAt
        };

    private sealed class Fixture
    {
        public Fixture(bool batchRepository = false)
        {
            if (batchRepository)
                BatchDocuments = Documents.As<IDocumentDraftBatchRepository>();

            Uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
            Uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.EnsureActiveTransaction());
            Locks.Setup(x => x.LockDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Types.Setup(x => x.TryGet("doc")).Returns(new DocumentTypeMetadata("doc", []));
            Validators.Setup(x => x.ResolveDraftValidators(It.IsAny<string>()))
                .Returns(Array.Empty<IDocumentDraftValidator>());
            Policies.Setup(x => x.Resolve(It.IsAny<string>())).Returns((IDocumentNumberingPolicy?)null);
            Storage.Setup(x => x.TryResolve(It.IsAny<string>())).Returns((IDocumentTypeStorage?)null);
            Documents.Setup(x => x.CreateAsync(It.IsAny<DocumentRecord>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Documents.Setup(x => x.UpdateDraftHeaderAsync(
                    It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var writeEngine = new DocumentWriteEngine(Uow.Object, Locks.Object, Storage.Object);
            Sut = new DocumentDraftService(
                Uow.Object,
                Locks.Object,
                Documents.Object,
                writeEngine,
                Validators.Object,
                Numbering.Object,
                Policies.Object,
                Types.Object,
                Audit.Object,
                new FixedTimeProvider(new DateTimeOffset(Now)));
        }

        public Mock<IUnitOfWork> Uow { get; } = new(MockBehavior.Loose);
        public Mock<IAdvisoryLockManager> Locks { get; } = new(MockBehavior.Loose);
        public Mock<IDocumentRepository> Documents { get; } = new(MockBehavior.Loose);
        public Mock<IDocumentDraftBatchRepository>? BatchDocuments { get; }
        public Mock<IDocumentTypeStorageResolver> Storage { get; } = new(MockBehavior.Loose);
        public Mock<IDocumentValidatorResolver> Validators { get; } = new(MockBehavior.Loose);
        public Mock<IDocumentNumberingAndTypedSyncService> Numbering { get; } = new(MockBehavior.Loose);
        public Mock<IDocumentNumberingPolicyResolver> Policies { get; } = new(MockBehavior.Loose);
        public Mock<NGB.Metadata.Documents.Storage.IDocumentTypeRegistry> Types { get; } = new(MockBehavior.Loose);
        public Mock<IAuditLogService> Audit { get; } = new(MockBehavior.Loose);
        public DocumentDraftService Sut { get; }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
