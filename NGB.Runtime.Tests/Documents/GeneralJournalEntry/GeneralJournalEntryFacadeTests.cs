using FluentAssertions;
using Moq;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Runtime.Documents.GeneralJournalEntry;
using Xunit;

namespace NGB.Runtime.Tests.Documents.GeneralJournalEntry;

public sealed class GeneralJournalEntryFacadeTests
{
    [Fact]
    public async Task DelegatingMembers_ForwardAllArguments()
    {
        var documentId = Guid.CreateVersion7();
        var sourceDocumentId = Guid.CreateVersion7();
        var basedOnDocumentIds = new[] { Guid.CreateVersion7() };
        var dateUtc = new DateTime(2026, 1, 21, 0, 0, 0, DateTimeKind.Utc);
        var header = new GeneralJournalEntryDraftHeaderUpdate(null, null, null, null, null, null);
        IReadOnlyList<GeneralJournalEntryDraftLineInput> lines = [];
        using var cancellation = new CancellationTokenSource();
        var service = new Mock<IGeneralJournalEntryDocumentService>();
        var facade = new GeneralJournalEntryFacade(service.Object);

        await facade.CreateDraftAsync(dateUtc, "creator", cancellation.Token, sourceDocumentId, basedOnDocumentIds);
        await facade.UpdateDraftHeaderAsync(documentId, header, "updater", cancellation.Token);
        await facade.ReplaceDraftLinesAsync(documentId, lines, "updater", cancellation.Token);
        await facade.SubmitAsync(documentId, "submitter", cancellation.Token);
        await facade.ApproveAsync(documentId, "approver", cancellation.Token);
        await facade.RejectAsync(documentId, "rejecter", "reason", cancellation.Token);
        await facade.PostApprovedAsync(documentId, "poster", cancellation.Token);
        await facade.ReversePostedAsync(documentId, dateUtc, "reverser", false, cancellation.Token);

        service.Verify(x => x.CreateDraftAsync(dateUtc, "creator", cancellation.Token, sourceDocumentId, basedOnDocumentIds), Times.Once);
        service.Verify(x => x.UpdateDraftHeaderAsync(documentId, header, "updater", cancellation.Token), Times.Once);
        service.Verify(x => x.ReplaceDraftLinesAsync(documentId, lines, "updater", cancellation.Token), Times.Once);
        service.Verify(x => x.SubmitAsync(documentId, "submitter", cancellation.Token), Times.Once);
        service.Verify(x => x.ApproveAsync(documentId, "approver", cancellation.Token), Times.Once);
        service.Verify(x => x.RejectAsync(documentId, "rejecter", "reason", cancellation.Token), Times.Once);
        service.Verify(x => x.PostApprovedAsync(documentId, "poster", cancellation.Token), Times.Once);
        service.Verify(x => x.ReversePostedAsync(documentId, dateUtc, "reverser", false, cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task GetDraftAsync_DelegatesAndReturnsExactSnapshot()
    {
        var documentId = Guid.CreateVersion7();
        var snapshot = new GeneralJournalEntryDraftSnapshot(null!, null!, []);
        using var cancellation = new CancellationTokenSource();
        var service = new Mock<IGeneralJournalEntryDocumentService>(MockBehavior.Strict);
        service.Setup(x => x.GetDraftAsync(documentId, cancellation.Token))
            .ReturnsAsync(snapshot);

        var result = await new GeneralJournalEntryFacade(service.Object)
            .GetDraftAsync(documentId, cancellation.Token);

        result.Should().BeSameAs(snapshot);
        service.VerifyAll();
    }

    [Fact]
    public async Task CreateAndPostApprovedAsync_Composes_Workflow_InOrder()
    {
        var dateUtc = new DateTime(2026, 01, 21, 0, 0, 0, DateTimeKind.Utc);
        const string initiatedBy = "INIT";
        const string submittedBy = "SUB";
        const string approvedBy = "APR";
        const string postedBy = "PST";

        var docId = Guid.CreateVersion7();

        var header = new GeneralJournalEntryDraftHeaderUpdate(
            JournalType: GeneralJournalEntryModels.JournalType.Standard,
            ReasonCode: "RC",
            Memo: "memo",
            ExternalReference: null,
            AutoReverse: false,
            AutoReverseOnUtc: null);

        var lines = new List<GeneralJournalEntryDraftLineInput>
        {
            new(
                Side: GeneralJournalEntryModels.LineSide.Debit,
                AccountId: Guid.CreateVersion7(),
                Amount: 10m,



                Memo: null),
            new(
                Side: GeneralJournalEntryModels.LineSide.Credit,
                AccountId: Guid.CreateVersion7(),
                Amount: 10m,



                Memo: null),
        };

        var svc = new Mock<IGeneralJournalEntryDocumentService>(MockBehavior.Strict);

        svc.Setup(x => x.CreateAndPostApprovedAsync(
                dateUtc,
                header,
                lines,
                initiatedBy,
                submittedBy,
                approvedBy,
                postedBy,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(docId);

        var facade = new GeneralJournalEntryFacade(svc.Object);

        var id = await facade.CreateAndPostApprovedAsync(
            dateUtc,
            header,
            lines,
            initiatedBy,
            submittedBy,
            approvedBy,
            postedBy);

        id.Should().Be(docId);
        svc.VerifyAll();
    }

    [Fact]
    public async Task CreateAndPostApprovedAsync_Skips_HeaderAndLines_When_NotProvided()
    {
        var dateUtc = new DateTime(2026, 01, 21, 0, 0, 0, DateTimeKind.Utc);
        const string initiatedBy = "INIT";
        const string submittedBy = "SUB";
        const string approvedBy = "APR";
        const string postedBy = "PST";

        var docId = Guid.CreateVersion7();

        var svc = new Mock<IGeneralJournalEntryDocumentService>(MockBehavior.Strict);

        svc.Setup(x => x.CreateAndPostApprovedAsync(
                dateUtc,
                null,
                null,
                initiatedBy,
                submittedBy,
                approvedBy,
                postedBy,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(docId);

        var facade = new GeneralJournalEntryFacade(svc.Object);

        var id = await facade.CreateAndPostApprovedAsync(
            dateUtc,
            header: null,
            lines: null,
            initiatedBy,
            submittedBy,
            approvedBy,
            postedBy);

        id.Should().Be(docId);
        svc.VerifyAll();
    }
}
