using FluentAssertions;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Contracts.Accounting;
using NGB.Core.Dimensions;
using NGB.Core.Dimensions.Enrichment;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Documents.Storage;
using NGB.Persistence.Accounts;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.GeneralJournalEntry;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.CurrentActor;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.Documents.GeneralJournalEntry.Exceptions;
using NGB.Runtime.Documents.Numbering;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Documents.GeneralJournalEntry;

public sealed class GeneralJournalEntryUiServiceFullCoverageTests
{
    private static readonly DateTime DateUtc = new(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Commands_WithNullRequests_FailFast()
    {
        var fixture = new Fixture();
        var id = Guid.CreateVersion7();

        var create = () => fixture.Sut.CreateDraftAsync(null!, fixture.Token);
        var update = () => fixture.Sut.UpdateHeaderAsync(id, null!, fixture.Token);
        var replace = () => fixture.Sut.ReplaceLinesAsync(id, null!, fixture.Token);
        var reject = () => fixture.Sut.RejectAsync(id, null!, fixture.Token);
        var reverse = () => fixture.Sut.ReversePostedAsync(id, null!, fixture.Token);

        (await create.Should().ThrowAsync<NgbArgumentRequiredException>()).Which.ParamName.Should().Be("request");
        (await update.Should().ThrowAsync<NgbArgumentRequiredException>()).Which.ParamName.Should().Be("request");
        (await replace.Should().ThrowAsync<NgbArgumentRequiredException>()).Which.ParamName.Should().Be("request");
        (await reject.Should().ThrowAsync<NgbArgumentRequiredException>()).Which.ParamName.Should().Be("request");
        (await reverse.Should().ThrowAsync<NgbArgumentRequiredException>()).Which.ParamName.Should().Be("request");
    }

    [Fact]
    public async Task SubmitAsync_WhenCurrentActorIsMissing_FailsBeforeFacadeCall()
    {
        var fixture = new Fixture(actor: null);

        var act = () => fixture.Sut.SubmitAsync(Guid.CreateVersion7(), fixture.Token);

        await act.Should().ThrowAsync<GeneralJournalEntryCurrentActorRequiredException>();
        fixture.Facade.Verify(
            x => x.SubmitAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("  Display Name  ", "mail@example.com", "subject", "Display Name")]
    [InlineData(null, "  mail@example.com  ", "subject", "mail@example.com")]
    [InlineData(null, null, "  subject  ", "subject")]
    public async Task SubmitAsync_UsesDeterministicActorDisplayFallback(
        string? displayName,
        string? email,
        string authSubject,
        string expected)
    {
        var actor = new ActorIdentity(authSubject, email, displayName);
        var fixture = new Fixture(actor);
        var id = Guid.CreateVersion7();
        string? capturedActor = null;
        fixture.Facade
            .Setup(x => x.SubmitAsync(id, It.IsAny<string>(), fixture.Token))
            .Callback<Guid, string, CancellationToken>((_, value, _) => capturedActor = value)
            .Returns(Task.CompletedTask);

        var act = () => fixture.Sut.SubmitAsync(id, fixture.Token);

        await act.Should().ThrowAsync<DocumentNotFoundException>();
        capturedActor.Should().Be(expected);
    }

    [Fact]
    public async Task CreateDraftAsync_WhenNumberIsBlank_EnsuresNumber_Commits_ThenBuildsDetails()
    {
        var fixture = new Fixture(Actor());
        var id = Guid.CreateVersion7();
        var locked = Document(id, number: "  ");
        fixture.Facade
            .Setup(x => x.CreateDraftAsync(DateUtc, "Actor", fixture.Token, null, null))
            .ReturnsAsync(id);
        fixture.Uow.Setup(x => x.BeginTransactionAsync(fixture.Token)).Returns(Task.CompletedTask);
        fixture.Documents.Setup(x => x.GetForUpdateAsync(id, fixture.Token)).ReturnsAsync(locked);
        fixture.Numbering
            .Setup(x => x.EnsureNumberAndSyncTypedAsync(locked, It.IsAny<DateTime>(), fixture.Token))
            .ReturnsAsync("GJE-1");
        fixture.Uow.Setup(x => x.CommitAsync(fixture.Token)).Returns(Task.CompletedTask);

        var act = () => fixture.Sut.CreateDraftAsync(new CreateGeneralJournalEntryDraftRequestDto(DateUtc), fixture.Token);

        await act.Should().ThrowAsync<DocumentNotFoundException>();
        fixture.Numbering.Verify(
            x => x.EnsureNumberAndSyncTypedAsync(locked, It.IsAny<DateTime>(), fixture.Token),
            Times.Once);
        fixture.Uow.Verify(x => x.CommitAsync(fixture.Token), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateDraftAsync_WhenLockedReadFails_RollsBackOnlyAnActiveTransaction(bool activeTransaction)
    {
        var fixture = new Fixture(Actor());
        var id = Guid.CreateVersion7();
        fixture.Facade
            .Setup(x => x.CreateDraftAsync(DateUtc, "Actor", fixture.Token, null, null))
            .ReturnsAsync(id);
        fixture.Uow.Setup(x => x.BeginTransactionAsync(fixture.Token)).Returns(Task.CompletedTask);
        fixture.Uow.SetupGet(x => x.HasActiveTransaction).Returns(activeTransaction);
        fixture.Documents
            .Setup(x => x.GetForUpdateAsync(id, fixture.Token))
            .ThrowsAsync(new InvalidOperationException("locked read failed"));
        fixture.Uow.Setup(x => x.RollbackAsync(fixture.Token)).Returns(Task.CompletedTask);

        var act = () => fixture.Sut.CreateDraftAsync(new CreateGeneralJournalEntryDraftRequestDto(DateUtc), fixture.Token);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("locked read failed");
        fixture.Uow.Verify(
            x => x.RollbackAsync(fixture.Token),
            activeTransaction ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task CreateDraftAsync_WhenCreatedDocumentCannotBeLocked_RollsBackAsNotFound()
    {
        var fixture = new Fixture(Actor());
        var id = Guid.CreateVersion7();
        fixture.Facade
            .Setup(x => x.CreateDraftAsync(DateUtc, "Actor", fixture.Token, null, null))
            .ReturnsAsync(id);
        fixture.Uow.Setup(x => x.BeginTransactionAsync(fixture.Token)).Returns(Task.CompletedTask);
        fixture.Uow.SetupGet(x => x.HasActiveTransaction).Returns(true);
        fixture.Documents.Setup(x => x.GetForUpdateAsync(id, fixture.Token)).ReturnsAsync((DocumentRecord?)null);
        fixture.Uow.Setup(x => x.RollbackAsync(fixture.Token)).Returns(Task.CompletedTask);

        var act = () => fixture.Sut.CreateDraftAsync(new CreateGeneralJournalEntryDraftRequestDto(DateUtc), fixture.Token);

        var exception = await act.Should().ThrowAsync<DocumentNotFoundException>();
        exception.Which.DocumentId.Should().Be(id);
        fixture.Uow.Verify(x => x.RollbackAsync(fixture.Token), Times.Once);
    }

    [Fact]
    public async Task UpdateHeaderAsync_WithNullJournalType_ForwardsNullWithoutCoercion()
    {
        var fixture = new Fixture();
        var id = Guid.CreateVersion7();
        GeneralJournalEntryDraftHeaderUpdate? captured = null;
        fixture.Facade
            .Setup(x => x.UpdateDraftHeaderAsync(id, It.IsAny<GeneralJournalEntryDraftHeaderUpdate>(), "editor", fixture.Token))
            .Callback<Guid, GeneralJournalEntryDraftHeaderUpdate, string, CancellationToken>(
                (_, update, _, _) => captured = update)
            .Returns(Task.CompletedTask);

        var act = () => fixture.Sut.UpdateHeaderAsync(
            id,
            new UpdateGeneralJournalEntryHeaderRequestDto("editor", JournalType: null),
            fixture.Token);

        await act.Should().ThrowAsync<DocumentNotFoundException>();
        captured.Should().NotBeNull();
        captured!.JournalType.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_CoversMissingDocument_MissingHeader_AndMinimalDetailsFallbacks()
    {
        var missingDocument = new Fixture();
        var missingId = Guid.CreateVersion7();
        Func<Task> getMissingDocument = () =>
            missingDocument.Sut.GetByIdAsync(missingId, missingDocument.Token);
        await getMissingDocument.Should().ThrowAsync<DocumentNotFoundException>();

        var missingHeader = new Fixture();
        var headerlessId = Guid.CreateVersion7();
        missingHeader.Documents
            .Setup(x => x.GetAsync(headerlessId, missingHeader.Token))
            .ReturnsAsync(Document(headerlessId, "GJE-1"));
        Func<Task> getMissingHeader = () =>
            missingHeader.Sut.GetByIdAsync(headerlessId, missingHeader.Token);
        await getMissingHeader.Should().ThrowAsync<DocumentNotFoundException>();

        var minimal = new Fixture();
        var id = Guid.CreateVersion7();
        minimal.ConfigureDetails(Document(id, number: null), Header(id));

        var details = await minimal.Sut.GetByIdAsync(id, minimal.Token);

        details.Document.Display.Should().Be("General Journal Entry 3/15/2026");
        details.Lines.Should().BeEmpty();
        details.Allocations.Should().BeEmpty();
        details.AccountContexts.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByIdAsync_CoversAccountDimensionAllocation_AndEmptyReversalFallbacks()
    {
        var fixture = new Fixture();
        var id = Guid.CreateVersion7();
        var presentAccount = new Account(Guid.CreateVersion7(), "1010", "Cash", AccountType.Asset);
        var missingAccountId = Guid.CreateVersion7();
        var presentSetId = Guid.CreateVersion7();
        var missingSetId = Guid.CreateVersion7();
        var firstDimension = new DimensionValue(Guid.CreateVersion7(), Guid.CreateVersion7());
        var secondDimension = new DimensionValue(Guid.CreateVersion7(), Guid.CreateVersion7());
        var thirdDimension = new DimensionValue(Guid.CreateVersion7(), Guid.CreateVersion7());
        var presentBag = new DimensionBag([firstDimension, secondDimension, thirdDimension]);
        fixture.ConfigureDetails(Document(id, " GJE-1 "), Header(id, Guid.Empty));
        fixture.GeneralJournal
            .Setup(x => x.GetLinesAsync(id, fixture.Token))
            .ReturnsAsync(
            [
                new GeneralJournalEntryLineRecord(
                    id,
                    1,
                    GeneralJournalEntryModels.LineSide.Debit,
                    presentAccount.Id,
                    10m,
                    "present",
                    presentSetId),
                new GeneralJournalEntryLineRecord(
                    id,
                    2,
                    GeneralJournalEntryModels.LineSide.Credit,
                    missingAccountId,
                    10m,
                    "missing",
                    missingSetId)
            ]);
        fixture.GeneralJournal
            .Setup(x => x.GetAllocationsAsync(id, fixture.Token))
            .ReturnsAsync([new GeneralJournalEntryAllocationRecord(id, 1, 1, 2, 10m)]);
        fixture.Accounts
            .Setup(x => x.GetAdminByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), fixture.Token))
            .ReturnsAsync(
            [
                new ChartOfAccountsAdminItem
                {
                    Account = presentAccount,
                    IsActive = true
                }
            ]);
        fixture.DimensionSets
            .Setup(x => x.GetBagsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), fixture.Token))
            .ReturnsAsync(new Dictionary<Guid, DimensionBag> { [presentSetId] = presentBag });
        fixture.DimensionValues
            .Setup(x => x.ResolveAsync(It.IsAny<IReadOnlyCollection<DimensionValueKey>>(), fixture.Token))
            .ReturnsAsync(new Dictionary<DimensionValueKey, string>
            {
                [new DimensionValueKey(firstDimension.DimensionId, firstDimension.ValueId)] = "Resolved",
                [new DimensionValueKey(secondDimension.DimensionId, secondDimension.ValueId)] = " "
            });

        var details = await fixture.Sut.GetByIdAsync(id, fixture.Token);

        details.Document.Display.Should().Be("General Journal Entry GJE-1 3/15/2026");
        details.Lines.Should().HaveCount(2);
        details.Lines[0].AccountDisplay.Should().Be("1010 — Cash");
        details.Lines[0].Dimensions.Select(x => x.Display).Should().BeEquivalentTo("Resolved", null, null);
        details.Lines[1].AccountDisplay.Should().Be(missingAccountId.ToString());
        details.Lines[1].Dimensions.Should().BeEmpty();
        details.Allocations.Should().ContainSingle();
        details.AccountContexts.Should().ContainSingle(x => x.AccountId == presentAccount.Id);
        details.Header.ReversalOfDocumentDisplay.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_WhenReversalDocumentIsMissing_ReturnsNullDisplay()
    {
        var fixture = new Fixture();
        var id = Guid.CreateVersion7();
        var missingReversalId = Guid.CreateVersion7();
        fixture.ConfigureDetails(Document(id, "GJE-1"), Header(id, missingReversalId));

        var details = await fixture.Sut.GetByIdAsync(id, fixture.Token);

        details.Header.ReversalOfDocumentId.Should().Be(missingReversalId);
        details.Header.ReversalOfDocumentDisplay.Should().BeNull();
        fixture.Documents.Verify(x => x.GetAsync(missingReversalId, fixture.Token), Times.Once);
    }

    private static ActorIdentity Actor() => new("subject", null, " Actor ");

    private static DocumentRecord Document(Guid id, string? number) => new()
    {
        Id = id,
        TypeCode = "general_journal_entry",
        Number = number,
        DateUtc = DateUtc,
        Status = DocumentStatus.Draft
    };

    private static GeneralJournalEntryHeaderRecord Header(Guid id, Guid? reversalOfDocumentId = null) => new(
        DocumentId: id,
        JournalType: GeneralJournalEntryModels.JournalType.Standard,
        Source: GeneralJournalEntryModels.Source.Manual,
        ApprovalState: GeneralJournalEntryModels.ApprovalState.Draft,
        ReasonCode: null,
        Memo: null,
        ExternalReference: null,
        AutoReverse: false,
        AutoReverseOnUtc: null,
        ReversalOfDocumentId: reversalOfDocumentId,
        InitiatedBy: null,
        InitiatedAtUtc: null,
        SubmittedBy: null,
        SubmittedAtUtc: null,
        ApprovedBy: null,
        ApprovedAtUtc: null,
        RejectedBy: null,
        RejectedAtUtc: null,
        RejectReason: null,
        PostedBy: null,
        PostedAtUtc: null,
        CreatedAtUtc: DateUtc,
        UpdatedAtUtc: DateUtc);

    private sealed class Fixture
    {
        public Fixture(ActorIdentity? actor = null)
        {
            CurrentActor.SetupGet(x => x.Current).Returns(actor);
            Sut = new GeneralJournalEntryUiService(
                Facade.Object,
                CurrentActor.Object,
                Documents.Object,
                GeneralJournal.Object,
                PageQuery.Object,
                Posting.Object,
                Uow.Object,
                Numbering.Object,
                DimensionSets.Object,
                DimensionValues.Object,
                Accounts.Object,
                Catalogs.Object,
                DocumentTypes.Object,
                TimeProvider.System);
        }

        public CancellationToken Token { get; } = new CancellationTokenSource().Token;
        public Mock<IGeneralJournalEntryFacade> Facade { get; } = new();
        public Mock<ICurrentActorContext> CurrentActor { get; } = new();
        public Mock<IDocumentRepository> Documents { get; } = new();
        public Mock<IGeneralJournalEntryRepository> GeneralJournal { get; } = new();
        public Mock<IGeneralJournalEntryUiQueryRepository> PageQuery { get; } = new();
        public Mock<IDocumentPostingService> Posting { get; } = new();
        public Mock<IUnitOfWork> Uow { get; } = new();
        public Mock<IDocumentNumberingAndTypedSyncService> Numbering { get; } = new();
        public Mock<IDimensionSetReader> DimensionSets { get; } = new();
        public Mock<IDimensionValueEnrichmentReader> DimensionValues { get; } = new();
        public Mock<IChartOfAccountsRepository> Accounts { get; } = new();
        public Mock<ICatalogTypeRegistry> Catalogs { get; } = new();
        public Mock<IDocumentTypeRegistry> DocumentTypes { get; } = new();
        public GeneralJournalEntryUiService Sut { get; }

        public void ConfigureDetails(DocumentRecord document, GeneralJournalEntryHeaderRecord header)
        {
            Documents.Setup(x => x.GetAsync(document.Id, Token)).ReturnsAsync(document);
            GeneralJournal.Setup(x => x.GetHeaderAsync(document.Id, Token)).ReturnsAsync(header);
            GeneralJournal
                .Setup(x => x.GetLinesAsync(document.Id, Token))
                .ReturnsAsync(Array.Empty<GeneralJournalEntryLineRecord>());
            GeneralJournal
                .Setup(x => x.GetAllocationsAsync(document.Id, Token))
                .ReturnsAsync(Array.Empty<GeneralJournalEntryAllocationRecord>());
        }
    }
}
