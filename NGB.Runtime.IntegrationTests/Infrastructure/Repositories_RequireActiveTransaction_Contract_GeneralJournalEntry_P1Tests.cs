using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Persistence.Documents.GeneralJournalEntry;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P1: The platform enforces transaction boundaries for all write operations.
/// This file extends the contract suite to General Journal Entry (GJE) typed storage repository.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class Repositories_RequireActiveTransaction_Contract_GeneralJournalEntry_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string TxnRequired = "This operation requires an active transaction.";

    [Fact]
    public async Task GeneralJournalEntryRepository_GetHeaderForUpdate_WithoutTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryRepository>();

        var act = () => repo.GetHeaderForUpdateAsync(Guid.CreateVersion7(), CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task GeneralJournalEntryRepository_UpsertHeader_WithoutTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryRepository>();

        var now = DateTime.UtcNow;
        var header = new GeneralJournalEntryHeaderRecord(
            DocumentId: Guid.CreateVersion7(),
            JournalType: GeneralJournalEntryModels.JournalType.Standard,
            Source: GeneralJournalEntryModels.Source.Manual,
            ApprovalState: GeneralJournalEntryModels.ApprovalState.Draft,
            ReasonCode: null,
            Memo: null,
            ExternalReference: null,
            AutoReverse: false,
            AutoReverseOnUtc: null,
            ReversalOfDocumentId: null,
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
            CreatedAtUtc: now,
            UpdatedAtUtc: now);

        var act = () => repo.UpsertHeaderAsync(header, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task GeneralJournalEntryRepository_TouchUpdatedAt_WithoutTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryRepository>();

        var act = () => repo.TouchUpdatedAtAsync(Guid.CreateVersion7(), DateTime.UtcNow, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task GeneralJournalEntryRepository_ReplaceLines_WithoutTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryRepository>();

        var documentId = Guid.CreateVersion7();
        var lines = new[]
        {
            new GeneralJournalEntryLineRecord(
                DocumentId: documentId,
                LineNo: 1,
                Side: GeneralJournalEntryModels.LineSide.Debit,
                AccountId: Guid.CreateVersion7(),
                Amount: 1m,
                Memo: "test")
        };

        var act = () => repo.ReplaceLinesAsync(documentId, lines, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task GeneralJournalEntryRepository_ReplaceAllocations_WithoutTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryRepository>();

        var documentId = Guid.CreateVersion7();
        var allocations = new[]
        {
            new GeneralJournalEntryAllocationRecord(
                DocumentId: documentId,
                EntryNo: 1,
                DebitLineNo: 1,
                CreditLineNo: 2,
                Amount: 1m)
        };

        var act = () => repo.ReplaceAllocationsAsync(documentId, allocations, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task GeneralJournalEntryRepository_ReadOperations_WithoutTransaction_DoNotThrow()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryRepository>();
        var documentId = Guid.CreateVersion7();

        // Header is optional by design.
        var header = await repo.GetHeaderAsync(documentId, CancellationToken.None);
        header.Should().BeNull();

        // Lines/allocations are empty by default.
        var lines = await repo.GetLinesAsync(documentId, CancellationToken.None);
        lines.Should().BeEmpty();

        var allocations = await repo.GetAllocationsAsync(documentId, CancellationToken.None);
        allocations.Should().BeEmpty();

        // Due-reversals query is safe without transaction as well.
        var due = await repo.GetDueSystemReversalsAsync(new DateOnly(2026, 1, 1), limit: 10, CancellationToken.None);
        due.Should().BeEmpty();

        var dueCandidates = await repo.GetDueSystemReversalCandidatesAsync(new DateOnly(2026, 1, 1), limit: 10, ct: CancellationToken.None);
        dueCandidates.Should().BeEmpty();
    }
}
