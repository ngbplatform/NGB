using FluentAssertions;
using Moq;
using NGB.Accounting.Documents;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Persistence.Documents.GeneralJournalEntry;
using NGB.Runtime.Documents.GeneralJournalEntry.Exceptions;
using NGB.Runtime.Documents.GeneralJournalEntry.Policies;
using NGB.Runtime.Documents.Workflow;
using Xunit;

namespace NGB.Runtime.Tests.Documents.GeneralJournalEntry;

public sealed class GeneralJournalEntryApprovalPolicyFullCoverageTests
{
    [Fact]
    public async Task EnsureCanPost_WrongDocumentType_ThrowsBeforeRepositoryCall()
    {
        var repository = new Mock<IGeneralJournalEntryRepository>(MockBehavior.Strict);
        var policy = new GeneralJournalEntryApprovalPolicy(repository.Object);

        var action = () => policy.EnsureCanPostAsync(Document("other"));

        await action.Should().ThrowAsync<DocumentTypeMismatchException>();
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EnsureCanPost_MissingTypedHeader_Throws()
    {
        var document = Document(AccountingDocumentTypeCodes.GeneralJournalEntry);
        var repository = new Mock<IGeneralJournalEntryRepository>(MockBehavior.Strict);
        repository.Setup(x => x.GetHeaderForUpdateAsync(document.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GeneralJournalEntryHeaderRecord?)null);
        var policy = new GeneralJournalEntryApprovalPolicy(repository.Object);

        var action = () => policy.EnsureCanPostAsync(document);

        await action.Should().ThrowAsync<GeneralJournalEntryTypedHeaderNotFoundException>();
    }

    [Fact]
    public async Task EnsureCanPost_UnapprovedHeader_ThrowsWorkflowStateMismatch()
    {
        var document = Document(AccountingDocumentTypeCodes.GeneralJournalEntry);
        var repository = Repository(document.Id, GeneralJournalEntryModels.ApprovalState.Submitted);
        var policy = new GeneralJournalEntryApprovalPolicy(repository.Object);

        var action = () => policy.EnsureCanPostAsync(document);

        await action.Should().ThrowAsync<DocumentWorkflowStateMismatchException>();
    }

    [Fact]
    public async Task EnsureCanPost_ApprovedHeader_AllowsCaseInsensitiveTypeCodeAndForwardsToken()
    {
        var document = Document(AccountingDocumentTypeCodes.GeneralJournalEntry.ToUpperInvariant());
        using var cancellation = new CancellationTokenSource();
        var repository = Repository(document.Id, GeneralJournalEntryModels.ApprovalState.Approved, cancellation.Token);
        var policy = new GeneralJournalEntryApprovalPolicy(repository.Object);

        await policy.EnsureCanPostAsync(document, cancellation.Token);

        policy.TypeCode.Should().Be(AccountingDocumentTypeCodes.GeneralJournalEntry);
        repository.VerifyAll();
    }

    private static Mock<IGeneralJournalEntryRepository> Repository(
        Guid documentId,
        GeneralJournalEntryModels.ApprovalState approvalState,
        CancellationToken token = default)
    {
        var repository = new Mock<IGeneralJournalEntryRepository>(MockBehavior.Strict);
        repository.Setup(x => x.GetHeaderForUpdateAsync(documentId, token))
            .ReturnsAsync(Header(documentId, approvalState));
        return repository;
    }

    private static DocumentRecord Document(string typeCode) => new()
    {
        Id = Guid.CreateVersion7(),
        TypeCode = typeCode,
        DateUtc = Utc,
        Status = DocumentStatus.Posted,
        CreatedAtUtc = Utc,
        UpdatedAtUtc = Utc
    };

    private static GeneralJournalEntryHeaderRecord Header(
        Guid documentId,
        GeneralJournalEntryModels.ApprovalState approvalState)
        => new(
            documentId,
            GeneralJournalEntryModels.JournalType.Standard,
            GeneralJournalEntryModels.Source.Manual,
            approvalState,
            null,
            null,
            null,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            Utc,
            Utc);

    private static DateTime Utc => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}
