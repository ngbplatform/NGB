using FluentAssertions;
using Moq;
using NGB.Core.AuditLog;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Definitions;
using NGB.Persistence.Documents;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Documents;

public sealed class DocumentRelationshipServiceFullCoverageTests
{
    private static readonly Guid Low = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid High = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    private static readonly DateTime Now = new(2026, 7, 8, 9, 10, 11, DateTimeKind.Utc);

    [Fact]
    public async Task PublicGuardsListsAndExistsCoverIdsCodesLengthAndBothResults()
    {
        var fixture = new Fixture();
        await ((Func<Task>)(() => fixture.Sut.CreateAsync(Guid.Empty, High, "direct")))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => fixture.Sut.CreateAsync(Low, Guid.Empty, "direct")))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => fixture.Sut.CreateAsync(Low, Low, "direct")))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        await ((Func<Task>)(() => fixture.Sut.CreateAsync(Low, High, " ")))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => fixture.Sut.CreateAsync(Low, High, "missing")))
            .Should().ThrowAsync<DocumentRelationshipTypeNotFoundException>();

        await ((Func<Task>)(() => fixture.Sut.ListOutgoingAsync(Guid.Empty)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => fixture.Sut.ListIncomingAsync(Guid.Empty)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => fixture.Sut.ExistsIncomingAsync(Guid.Empty, "direct")))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => fixture.Sut.ExistsIncomingAsync(High, " ")))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => fixture.Sut.ExistsIncomingAsync(High, new string('x', 129))))
            .Should().ThrowAsync<NgbArgumentInvalidException>();

        var record = Relationship(Low, High, "Direct");
        fixture.Relationships.Setup(x => x.ListOutgoingAsync(Low, It.IsAny<CancellationToken>())).ReturnsAsync([record]);
        fixture.Relationships.Setup(x => x.ListIncomingAsync(High, It.IsAny<CancellationToken>())).ReturnsAsync([record]);
        fixture.Relationships.SetupSequence(x => x.GetSingleIncomingByCodeNormAsync(
                High, "direct", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRelationshipRecord?)null)
            .ReturnsAsync(record);
        (await fixture.Sut.ListOutgoingAsync(Low)).Should().ContainSingle();
        (await fixture.Sut.ListIncomingAsync(High)).Should().ContainSingle();
        (await fixture.Sut.ExistsIncomingAsync(High, " Direct ")).Should().BeFalse();
        (await fixture.Sut.ExistsIncomingAsync(High, "DIRECT")).Should().BeTrue();
    }

    [Fact]
    public async Task Create_CoversLockAndLoadOrderMissingRowsAllowedTypeAndDraftGuards()
    {
        var missingFirst = new Fixture(setupDocuments: false);
        missingFirst.Documents.Setup(x => x.GetForUpdateAsync(Low, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null);
        await ((Func<Task>)(() => missingFirst.Sut.CreateAsync(High, Low, "direct")))
            .Should().ThrowAsync<DocumentNotFoundException>();

        var missingSecond = new Fixture(setupDocuments: false);
        missingSecond.Documents.Setup(x => x.GetForUpdateAsync(Low, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Document(Low, "b"));
        missingSecond.Documents.Setup(x => x.GetForUpdateAsync(High, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null);
        await ((Func<Task>)(() => missingSecond.Sut.CreateAsync(Low, High, "direct")))
            .Should().ThrowAsync<DocumentNotFoundException>();

        await RejectCreate("from-only", "b", "b", "not_allowed_from_type");
        await RejectCreate("to-only", "a", "a", "not_allowed_to_type");
        await RejectCreate("bidi-reverse-from", "a", "b", "bidirectional_reverse_not_allowed_from_type");
        await RejectCreate("bidi-reverse-to", "a", "b", "bidirectional_reverse_not_allowed_to_type");
        await RejectCreate("direct", "a", "b", "from_document_must_be_draft", DocumentStatus.Posted);
        await RejectCreate("bidi", "a", "b", "bidirectional_requires_both_draft",
            toStatus: DocumentStatus.Posted);
    }

    [Fact]
    public async Task Create_CoversCycleAndEveryCardinalityConflictOrSameEdgeAllowance()
    {
        var cycle = new Fixture();
        cycle.Relationships.Setup(x => x.ExistsPathAsync(
                High, Low, "direct", 64, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await ((Func<Task>)(() => cycle.Sut.CreateAsync(Low, High, "direct")))
            .Should().ThrowAsync<DocumentRelationshipValidationException>()
            .WithMessage("*cycle_detected*");

        var outgoingConflict = new Fixture();
        outgoingConflict.Relationships.Setup(x => x.GetSingleOutgoingByCodeNormAsync(
                Low, "one", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Relationship(Low, Guid.NewGuid(), "one"));
        await ((Func<Task>)(() => outgoingConflict.Sut.CreateAsync(Low, High, "one")))
            .Should().ThrowAsync<DocumentRelationshipValidationException>()
            .WithMessage("*cardinality_max_outgoing_per_from*");

        var incomingConflict = new Fixture();
        incomingConflict.Relationships.Setup(x => x.GetSingleOutgoingByCodeNormAsync(
                Low, "one", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Relationship(Low, High, "one"));
        incomingConflict.Relationships.Setup(x => x.GetSingleIncomingByCodeNormAsync(
                High, "one", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Relationship(Guid.NewGuid(), High, "one"));
        await ((Func<Task>)(() => incomingConflict.Sut.CreateAsync(Low, High, "one")))
            .Should().ThrowAsync<DocumentRelationshipValidationException>()
            .WithMessage("*cardinality_max_incoming_per_to*");

        var same = new Fixture();
        same.Relationships.Setup(x => x.GetSingleOutgoingByCodeNormAsync(
                Low, "one", It.IsAny<CancellationToken>())).ReturnsAsync(Relationship(Low, High, "one"));
        same.Relationships.Setup(x => x.GetSingleIncomingByCodeNormAsync(
                High, "one", It.IsAny<CancellationToken>())).ReturnsAsync(Relationship(Low, High, "one"));
        same.Relationships.Setup(x => x.TryCreateAsync(It.IsAny<DocumentRelationshipRecord>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(false);
        (await same.Sut.CreateAsync(Low, High, "one")).Should().BeFalse();

        var noExisting = new Fixture();
        noExisting.Relationships.Setup(x => x.TryCreateAsync(It.IsAny<DocumentRelationshipRecord>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(false);
        (await noExisting.Sut.CreateAsync(Low, High, "one")).Should().BeFalse();
    }

    [Fact]
    public async Task Create_DirectedAndBidirectionalCoverCreatedNoopAuditAndExternalTransaction()
    {
        var directed = new Fixture();
        directed.Relationships.Setup(x => x.TryCreateAsync(It.IsAny<DocumentRelationshipRecord>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(true);
        (await directed.Sut.CreateAsync(Low, High, " DIRECT ")).Should().BeTrue();
        directed.Locks.Verify(x => x.LockDocumentAsync(Low, It.IsAny<CancellationToken>()), Times.Once);
        directed.Locks.Verify(x => x.LockDocumentAsync(High, It.IsAny<CancellationToken>()), Times.Once);
        directed.Audit.Verify(x => x.WriteAsync(
            AuditEntityKind.DocumentRelationship, It.IsAny<Guid>(), AuditActionCodes.DocumentRelationshipCreate,
            It.IsAny<IReadOnlyList<AuditFieldChange>>(), It.IsAny<object>(), null,
            It.IsAny<CancellationToken>()), Times.Once);

        var bidi = new Fixture();
        bidi.Relationships.SetupSequence(x => x.TryCreateAsync(
                It.IsAny<DocumentRelationshipRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)
            .ReturnsAsync(true);
        bidi.Uow.SetupGet(x => x.HasActiveTransaction).Returns(true);
        bidi.Uow.Setup(x => x.EnsureActiveTransaction());
        (await bidi.Sut.CreateAsync(High, Low, "bidi", manageTransaction: false)).Should().BeTrue();
        bidi.Relationships.Verify(x => x.ExistsPathAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);

        var unrestricted = new Fixture();
        unrestricted.Relationships.Setup(x => x.TryCreateAsync(
            It.IsAny<DocumentRelationshipRecord>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        (await unrestricted.Sut.CreateAsync(Low, High, "bidi-open")).Should().BeFalse();
    }

    [Fact]
    public async Task CreateMany_BatchesLocksDocumentReadsRelationshipWritesAndAudit()
    {
        var fixture = new Fixture();
        var secondTarget = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        fixture.Documents.Setup(x => x.GetForUpdateByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DocumentRecord>
            {
                [Low] = Document(Low, "a"),
                [High] = Document(High, "b"),
                [secondTarget] = Document(secondTarget, "b")
            });
        fixture.Relationships.Setup(x => x.TryCreateManyAsync(
                It.IsAny<IReadOnlyList<DocumentRelationshipRecord>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DocumentRelationshipRecord> records, CancellationToken _) =>
                records.Select(static record => record.Id).ToArray());

        var created = await fixture.Sut.CreateManyAsync(
        [
            new DocumentRelationshipCreateRequest(Low, High, "direct"),
            new DocumentRelationshipCreateRequest(Low, secondTarget, " DIRECT "),
            new DocumentRelationshipCreateRequest(Low, High, "direct")
        ]);

        created.Should().Be(2);
        fixture.Documents.Verify(x => x.GetForUpdateByIdsAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 3), It.IsAny<CancellationToken>()), Times.Once);
        fixture.Relationships.Verify(x => x.TryCreateManyAsync(
            It.Is<IReadOnlyList<DocumentRelationshipRecord>>(records => records.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Audit.Verify(x => x.WriteBatchAsync(
            It.Is<IReadOnlyList<AuditLogWriteRequest>>(requests => requests.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateMany_ValidatesBatchAndDetectsMissingDocumentsAndBatchCycles()
    {
        var fixture = new Fixture();
        await ((Func<Task>)(() => fixture.Sut.CreateManyAsync(null!)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        (await fixture.Sut.CreateManyAsync([])).Should().Be(0);
        await ((Func<Task>)(() => fixture.Sut.CreateManyAsync([null!])))
            .Should().ThrowAsync<NgbArgumentInvalidException>();

        fixture.Documents.Setup(x => x.GetForUpdateByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DocumentRecord> { [Low] = Document(Low) });
        await ((Func<Task>)(() => fixture.Sut.CreateManyAsync(
                [new DocumentRelationshipCreateRequest(Low, High, "bidi-open")])))
            .Should().ThrowAsync<DocumentNotFoundException>();

        fixture.Documents.Setup(x => x.GetForUpdateByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DocumentRecord>
            {
                [Low] = Document(Low),
                [High] = Document(High)
            });
        await ((Func<Task>)(() => fixture.Sut.CreateManyAsync(
        [
            new DocumentRelationshipCreateRequest(Low, High, "direct-open"),
            new DocumentRelationshipCreateRequest(High, Low, "direct-open")
        ]))).Should().ThrowAsync<DocumentRelationshipValidationException>()
            .WithMessage("*cycle_detected*");
    }

    [Fact]
    public async Task Delete_CoversMissingExistingRaceSuccessDirectedAndBidirectional()
    {
        var id = NGB.Core.Documents.DeterministicDocumentRelationshipId.FromNormalizedCode(Low, "direct", High);
        var fixture = new Fixture();
        fixture.Relationships.SetupSequence(x => x.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRelationshipRecord?)null)
            .ReturnsAsync(Relationship(Low, High, "direct", id))
            .ReturnsAsync(Relationship(Low, High, "direct", id));
        fixture.Relationships.SetupSequence(x => x.TryDeleteAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)
            .ReturnsAsync(true);
        (await fixture.Sut.DeleteAsync(Low, High, "direct")).Should().BeFalse();
        (await fixture.Sut.DeleteAsync(Low, High, "direct")).Should().BeFalse();
        (await fixture.Sut.DeleteAsync(Low, High, "direct")).Should().BeTrue();
        fixture.Audit.Verify(x => x.WriteAsync(
            AuditEntityKind.DocumentRelationship, id, AuditActionCodes.DocumentRelationshipDelete,
            It.IsAny<IReadOnlyList<AuditFieldChange>>(), It.IsAny<object>(), null,
            It.IsAny<CancellationToken>()), Times.Once);

        var bidi = new Fixture();
        bidi.Relationships.Setup(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid relationshipId, CancellationToken _) => Relationship(Low, High, "bidi", relationshipId));
        bidi.Relationships.Setup(x => x.TryDeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        (await bidi.Sut.DeleteAsync(Low, High, "bidi")).Should().BeTrue();
        bidi.Relationships.Verify(x => x.TryDeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static async Task RejectCreate(
        string relationshipCode,
        string fromType,
        string toType,
        string reason,
        DocumentStatus fromStatus = DocumentStatus.Draft,
        DocumentStatus toStatus = DocumentStatus.Draft)
    {
        var fixture = new Fixture(setupDocuments: false);
        fixture.Documents.Setup(x => x.GetForUpdateAsync(Low, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Document(Low, fromType, fromStatus));
        fixture.Documents.Setup(x => x.GetForUpdateAsync(High, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Document(High, toType, toStatus));
        await ((Func<Task>)(() => fixture.Sut.CreateAsync(Low, High, relationshipCode)))
            .Should().ThrowAsync<DocumentRelationshipValidationException>()
            .WithMessage($"*{reason}*");
    }

    private static DocumentRecord Document(
        Guid id,
        string type = "a",
        DocumentStatus status = DocumentStatus.Draft)
        => new()
        {
            Id = id,
            TypeCode = type,
            DateUtc = Now,
            Status = status,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };

    private static DocumentRelationshipRecord Relationship(
        Guid from,
        Guid to,
        string code,
        Guid? id = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            FromDocumentId = from,
            ToDocumentId = to,
            RelationshipCode = code,
            RelationshipCodeNorm = code.ToLowerInvariant(),
            CreatedAtUtc = Now
        };

    private sealed class Fixture
    {
        public Fixture(bool setupDocuments = true)
        {
            Uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
            Uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Locks.Setup(x => x.LockDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            if (setupDocuments)
            {
                Documents.Setup(x => x.GetForUpdateAsync(Low, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Document(Low, "a"));
                Documents.Setup(x => x.GetForUpdateAsync(High, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Document(High, "b"));
            }
            Relationships.Setup(x => x.ExistsPathAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(false);
            Relationships.Setup(x => x.FindCycleCreatingRequestIndexesAsync(
                    It.IsAny<IReadOnlyList<DocumentRelationshipCycleCheck>>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            Relationships.Setup(x => x.GetCardinalityConflictsAsync(
                    It.IsAny<IReadOnlyList<DocumentRelationshipCardinalityCheck>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            Sut = new DocumentRelationshipService(
                Definitions(), Uow.Object, Locks.Object, Documents.Object, Relationships.Object,
                Audit.Object, new FixedTimeProvider(new DateTimeOffset(Now)));
        }

        public Mock<IUnitOfWork> Uow { get; } = new(MockBehavior.Loose);
        public Mock<IAdvisoryLockManager> Locks { get; } = new(MockBehavior.Loose);
        public Mock<IDocumentRepository> Documents { get; } = new(MockBehavior.Loose);
        public Mock<IDocumentRelationshipRepository> Relationships { get; } = new(MockBehavior.Loose);
        public Mock<IAuditLogService> Audit { get; } = new(MockBehavior.Loose);
        public DocumentRelationshipService Sut { get; }
    }

    private static DefinitionsRegistry Definitions()
    {
        var builder = new DefinitionsBuilder();
        builder.AddDocumentRelationshipType("direct", x => x.Name("Direct").ManyToMany()
            .AllowFromDocumentTypes("a").AllowToDocumentTypes("b"));
        builder.AddDocumentRelationshipType("bidi", x => x.Name("Bidi").Bidirectional().ManyToMany()
            .AllowFromDocumentTypes("a", "b").AllowToDocumentTypes("a", "b"));
        builder.AddDocumentRelationshipType("bidi-open", x => x.Name("Bidi Open").Bidirectional().ManyToMany());
        builder.AddDocumentRelationshipType("from-only", x => x.Name("From").ManyToMany().AllowFromDocumentTypes("a"));
        builder.AddDocumentRelationshipType("to-only", x => x.Name("To").ManyToMany().AllowToDocumentTypes("b"));
        builder.AddDocumentRelationshipType("bidi-reverse-from", x => x.Name("Reverse From").Bidirectional().ManyToMany()
            .AllowFromDocumentTypes("a").AllowToDocumentTypes("a", "b"));
        builder.AddDocumentRelationshipType("bidi-reverse-to", x => x.Name("Reverse To").Bidirectional().ManyToMany()
            .AllowFromDocumentTypes("a", "b").AllowToDocumentTypes("b"));
        builder.AddDocumentRelationshipType("one", x => x.Name("One").OneToOne());
        builder.AddDocumentRelationshipType("direct-open", x => x.Name("Direct Open").ManyToMany());
        return builder.Build();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
