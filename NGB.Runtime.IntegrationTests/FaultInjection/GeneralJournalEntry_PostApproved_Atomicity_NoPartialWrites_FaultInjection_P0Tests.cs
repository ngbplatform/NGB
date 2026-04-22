using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Core.Documents;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.GeneralJournalEntry;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Posting;
using NGB.Runtime.IntegrationTests.Reporting;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.FaultInjection;

[Collection(PostgresCollection.Name)]
public sealed class GeneralJournalEntry_PostApproved_Atomicity_NoPartialWrites_FaultInjection_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task PostApprovedAsync_WhenAllocationsWriteFailsAfterAccountingMovements_RollsBackEverything_AndRetrySucceeds()
    {
        await Fixture.ResetDatabaseAsync();

        var docDateUtc = new DateTime(2026, 03, 10, 12, 0, 0, DateTimeKind.Utc);

        // Baseline state: Draft -> Submitted -> Approved (no posting yet)
        using var goodHost = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(goodHost);

        Guid documentId;
        await using (var scope = goodHost.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();

            documentId = await gje.CreateDraftAsync(docDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

            await gje.UpdateDraftHeaderAsync(
                documentId,
                new GeneralJournalEntryDraftHeaderUpdate(
                    JournalType: null,
                    ReasonCode: "ATOMICITY",
                    Memo: "Atomicity test",
                    ExternalReference: null,
                    AutoReverse: false,
                    AutoReverseOnUtc: null),
                updatedBy: "u1",
                ct: CancellationToken.None);

            await gje.ReplaceDraftLinesAsync(
                documentId,
                new[]
                {
                    new GeneralJournalEntryDraftLineInput(
                        Side: GeneralJournalEntryModels.LineSide.Debit,
                        AccountId: cashId,
                        Amount: 100m,
                        Memo: null),
                    new GeneralJournalEntryDraftLineInput(
                        Side: GeneralJournalEntryModels.LineSide.Credit,
                        AccountId: revenueId,
                        Amount: 100m,
                        Memo: null),
                },
                updatedBy: "u1",
                ct: CancellationToken.None);

            await gje.SubmitAsync(documentId, submittedBy: "u1", ct: CancellationToken.None);
            await gje.ApproveAsync(documentId, approvedBy: "u2", ct: CancellationToken.None);
        }

        // Faulty host: allocations writer throws AFTER writing (still within the same DB transaction).
        using var badHost = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => { services.Decorate<IGeneralJournalEntryRepository, ThrowAfterNonEmptyAllocationsWriteGeneralJournalEntryRepository>(); });

        Func<Task> act = async () =>
        {
            await using var scope = badHost.Services.CreateAsyncScope();
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            await gje.PostApprovedAsync(documentId, postedBy: "u2", ct: CancellationToken.None);
        };

        var ex = await act.Should().ThrowAsync<NgbUnexpectedException>();

        ex.Which.Message.Should().Be("Unexpected internal error.");
        ex.Which.InnerException.Should().BeOfType<NotSupportedException>()
            .Which.Message.Should().StartWith("Simulated allocations write failure");

        // Assert: EVERYTHING was rolled back.
        await using (var scope = goodHost.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var doc = await docs.GetAsync(documentId, CancellationToken.None);
            doc.Should().NotBeNull();
            doc!.Status.Should().Be(DocumentStatus.Draft);
            doc.PostedAtUtc.Should().BeNull();

            var repo = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryRepository>();
            var header = await repo.GetHeaderAsync(documentId, CancellationToken.None);
            header.Should().NotBeNull();
            header!.PostedAtUtc.Should().BeNull();
            header.PostedBy.Should().BeNull();

            (await repo.GetAllocationsAsync(documentId, CancellationToken.None))
                .Should().BeEmpty("failed post must not persist allocation map");

            var postingLogReader = scope.ServiceProvider.GetRequiredService<IPostingStateReader>();
            (await CountPostingLogRowsAsync(postingLogReader, documentId, PostingOperation.Post))
                .Should().Be(0, "failed post must not leave posting_log rows");
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var regCount = (int)(await new NpgsqlCommand(
                "SELECT COUNT(*)::int FROM accounting_register_main WHERE document_id = @d",
                conn)
            {
                Parameters = { new("d", documentId) }
            }.ExecuteScalarAsync(CancellationToken.None))!;

            var allocCount = (int)(await new NpgsqlCommand(
                "SELECT COUNT(*)::int FROM doc_general_journal_entry__allocations WHERE document_id = @d",
                conn)
            {
                Parameters = { new("d", documentId) }
            }.ExecuteScalarAsync(CancellationToken.None))!;

            regCount.Should().Be(0, "failed post must not persist accounting movements");
            allocCount.Should().Be(0, "failed post must not persist allocations");
        }

        // Retry with a normal host (same DB) must succeed.
        using var retryHost = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using (var scope = retryHost.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            await gje.PostApprovedAsync(documentId, postedBy: "u2", ct: CancellationToken.None);
        }

        await using (var scope = retryHost.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            (await docs.GetAsync(documentId, CancellationToken.None))!.Status.Should().Be(DocumentStatus.Posted);

            var repo = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryRepository>();
            (await repo.GetAllocationsAsync(documentId, CancellationToken.None)).Should().HaveCount(1);

            var postingLogReader = scope.ServiceProvider.GetRequiredService<IPostingStateReader>();
            (await CountPostingLogRowsAsync(postingLogReader, documentId, PostingOperation.Post))
                .Should().Be(1);
        }
    }

    private static async Task<int> CountPostingLogRowsAsync(
        IPostingStateReader reader,
        Guid documentId,
        PostingOperation operation)
    {
        var page = await reader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddHours(-1),
            ToUtc = DateTime.UtcNow.AddHours(1),
            DocumentId = documentId,
            Operation = operation,
            PageSize = 50
        }, CancellationToken.None);

        return page.Records.Count;
    }

    private sealed class ThrowAfterNonEmptyAllocationsWriteGeneralJournalEntryRepository(IGeneralJournalEntryRepository inner)
        : IGeneralJournalEntryRepository
    {
        public Task<GeneralJournalEntryHeaderRecord?> GetHeaderAsync(Guid documentId, CancellationToken ct = default)
            => inner.GetHeaderAsync(documentId, ct);

        public Task<GeneralJournalEntryHeaderRecord?> GetHeaderForUpdateAsync(Guid documentId, CancellationToken ct = default)
            => inner.GetHeaderForUpdateAsync(documentId, ct);

        public Task UpsertHeaderAsync(GeneralJournalEntryHeaderRecord header, CancellationToken ct = default)
            => inner.UpsertHeaderAsync(header, ct);

        public Task TouchUpdatedAtAsync(Guid documentId, DateTime updatedAtUtc, CancellationToken ct = default)
            => inner.TouchUpdatedAtAsync(documentId, updatedAtUtc, ct);

        public Task<IReadOnlyList<GeneralJournalEntryLineRecord>> GetLinesAsync(Guid documentId, CancellationToken ct = default)
            => inner.GetLinesAsync(documentId, ct);

        public Task ReplaceLinesAsync(Guid documentId, IReadOnlyList<GeneralJournalEntryLineRecord> lines, CancellationToken ct = default)
            => inner.ReplaceLinesAsync(documentId, lines, ct);

        public Task<IReadOnlyList<GeneralJournalEntryAllocationRecord>> GetAllocationsAsync(Guid documentId, CancellationToken ct = default)
            => inner.GetAllocationsAsync(documentId, ct);

        public async Task ReplaceAllocationsAsync(Guid documentId, IReadOnlyList<GeneralJournalEntryAllocationRecord> allocations, CancellationToken ct = default)
        {
            await inner.ReplaceAllocationsAsync(documentId, allocations, ct);

            if (allocations.Count > 0)
                throw new NotSupportedException("Simulated allocations write failure");
        }

        public Task<Guid?> TryGetSystemReversalByOriginalAsync(Guid originalDocumentId, CancellationToken ct = default)
            => inner.TryGetSystemReversalByOriginalAsync(originalDocumentId, ct);

        public Task<IReadOnlyList<Guid>> GetDueSystemReversalsAsync(DateOnly utcDate, int limit, CancellationToken ct = default)
            => inner.GetDueSystemReversalsAsync(utcDate, limit, ct);

        public Task<IReadOnlyList<GeneralJournalEntryDueSystemReversalCandidate>> GetDueSystemReversalCandidatesAsync(
            DateOnly utcDate,
            int limit,
            DateTime? afterDateUtc = null,
            Guid? afterDocumentId = null,
            CancellationToken ct = default)
            => inner.GetDueSystemReversalCandidatesAsync(utcDate, limit, afterDateUtc, afterDocumentId, ct);
    }
}
