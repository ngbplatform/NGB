using FluentAssertions;
using Moq;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Runtime.Documents.GeneralJournalEntry;
using Xunit;

namespace NGB.Runtime.Tests.Documents.GeneralJournalEntry;

public sealed class GeneralJournalEntryFacadeTests
{
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
