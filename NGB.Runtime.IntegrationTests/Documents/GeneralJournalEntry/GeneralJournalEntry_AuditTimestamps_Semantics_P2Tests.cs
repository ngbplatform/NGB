using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Core.Documents;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.GeneralJournalEntry;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.Documents.GeneralJournalEntry.Exceptions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.GeneralJournalEntry;

/// <summary>
/// P2: Audit invariants.
/// - CreatedAtUtc is immutable for both the common document registry and the typed GJE header.
/// - Successful writes must touch UpdatedAtUtc (and do so consistently across registries).
/// - Failed operations (rolled back) must not touch UpdatedAtUtc.
///
/// Why:
/// - this is the backbone for UI caches, optimistic refresh, and auditability.
/// - prevents silent "touch" on failed validation paths.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GeneralJournalEntry_AuditTimestamps_Semantics_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task UpdateDraftHeader_Success_TouchesUpdatedAt_InDocumentsAndTypedHeader_ButNotCreatedAt()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid docId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            docId = await gje.CreateDraftAsync(
                dateUtc: new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc),
                initiatedBy: "u1",
                ct: CancellationToken.None);
        }

        await BackdateTimestampsAsync(Fixture.ConnectionString, docId, T0);

        var (doc0, header0) = await LoadDocAndHeaderAsync(host, docId);
        doc0.CreatedAtUtc.Should().Be(T0);
        doc0.UpdatedAtUtc.Should().Be(T0);
        header0.CreatedAtUtc.Should().Be(T0);
        header0.UpdatedAtUtc.Should().Be(T0);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            await gje.UpdateDraftHeaderAsync(
                docId,
                new GeneralJournalEntryDraftHeaderUpdate(
                    JournalType: null,
                    ReasonCode: "AUDIT",
                    Memo: "Audit timestamps",
                    ExternalReference: null,
                    AutoReverse: false,
                    AutoReverseOnUtc: null),
                updatedBy: "u1",
                ct: CancellationToken.None);
        }

        var (doc1, header1) = await LoadDocAndHeaderAsync(host, docId);

        doc1.CreatedAtUtc.Should().Be(T0, "CreatedAtUtc must be immutable");
        header1.CreatedAtUtc.Should().Be(T0, "typed header CreatedAtUtc must be immutable");

        doc1.UpdatedAtUtc.Should().BeAfter(T0);
        header1.UpdatedAtUtc.Should().Be(doc1.UpdatedAtUtc, "service touches both registries using the same now");
    }

    [Fact]
    public async Task ReplaceDraftLines_Success_TouchesUpdatedAt_InDocumentsAndTypedHeader()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId) = await EnsureMinimalAccountsAsync(host);

        var docDateUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        Guid docId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            docId = await gje.CreateDraftAsync(docDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

            // Make header business-valid (not required for ReplaceDraftLines, but keeps later steps consistent).
            await gje.UpdateDraftHeaderAsync(
                docId,
                new GeneralJournalEntryDraftHeaderUpdate(
                    JournalType: null,
                    ReasonCode: "LINES",
                    Memo: "Replace lines touches timestamps",
                    ExternalReference: null,
                    AutoReverse: false,
                    AutoReverseOnUtc: null),
                updatedBy: "u1",
                ct: CancellationToken.None);
        }

        await BackdateTimestampsAsync(Fixture.ConnectionString, docId, T0);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            await gje.ReplaceDraftLinesAsync(
                docId,
                new[]
                {
                    new GeneralJournalEntryDraftLineInput(
                        Side: GeneralJournalEntryModels.LineSide.Debit,
                        AccountId: cashId,
                        Amount: 10m,
                        Memo: null),
                    new GeneralJournalEntryDraftLineInput(
                        Side: GeneralJournalEntryModels.LineSide.Credit,
                        AccountId: revenueId,
                        Amount: 10m,
                        Memo: null),
                },
                updatedBy: "u1",
                ct: CancellationToken.None);
        }

        var (doc1, header1) = await LoadDocAndHeaderAsync(host, docId);
        doc1.CreatedAtUtc.Should().Be(T0);
        header1.CreatedAtUtc.Should().Be(T0);

        doc1.UpdatedAtUtc.Should().BeAfter(T0);
        header1.UpdatedAtUtc.Should().Be(doc1.UpdatedAtUtc);
    }

    [Fact]
    public async Task PostApproved_SetsPostedAt_ToSameValueAsUpdatedAt_AndNoOpDoesNotChangeUpdatedAt()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId) = await EnsureMinimalAccountsAsync(host);

        var docDateUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        Guid docId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            docId = await gje.CreateDraftAsync(docDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

            await gje.UpdateDraftHeaderAsync(
                docId,
                new GeneralJournalEntryDraftHeaderUpdate(
                    JournalType: null,
                    ReasonCode: "POST",
                    Memo: "Posting touches timestamps",
                    ExternalReference: null,
                    AutoReverse: false,
                    AutoReverseOnUtc: null),
                updatedBy: "u1",
                ct: CancellationToken.None);

            await gje.ReplaceDraftLinesAsync(
                docId,
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

            await gje.SubmitAsync(docId, submittedBy: "u1", ct: CancellationToken.None);
            await gje.ApproveAsync(docId, approvedBy: "u2", ct: CancellationToken.None);

            // Force deterministic CreatedAtUtc baseline for the immutability assertions.
            // UpdatedAtUtc will be changed by posting anyway.
            await BackdateTimestampsAsync(Fixture.ConnectionString, docId, T0);

            await gje.PostApprovedAsync(docId, postedBy: "u2", ct: CancellationToken.None);
        }

        var (docPosted, headerPosted) = await LoadDocAndHeaderAsync(host, docId);

        docPosted.Status.Should().Be(DocumentStatus.Posted);
        docPosted.CreatedAtUtc.Should().Be(T0);
        headerPosted.CreatedAtUtc.Should().Be(T0);

        docPosted.PostedAtUtc.Should().NotBeNull();
        docPosted.PostedAtUtc.Should().Be(docPosted.UpdatedAtUtc, "UpdateStatusAsync sets posted_at_utc and updated_at_utc to the same now");
        headerPosted.PostedAtUtc.Should().Be(docPosted.UpdatedAtUtc, "typed header audit uses the same now as documents registry");

        var updatedAtAfterPost = docPosted.UpdatedAtUtc;

        // No-op path: already posted should not change UpdatedAtUtc.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            await gje.PostApprovedAsync(docId, postedBy: "u2", ct: CancellationToken.None);
        }

        var (docAfterNoOp, _) = await LoadDocAndHeaderAsync(host, docId);
        docAfterNoOp.UpdatedAtUtc.Should().Be(updatedAtAfterPost);
    }

    [Fact]
    public async Task Rollback_InvalidHeaderUpdate_DoesNotTouchUpdatedAt()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid docId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            docId = await gje.CreateDraftAsync(
                new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc),
                initiatedBy: "u1",
                ct: CancellationToken.None);
        }

        await BackdateTimestampsAsync(Fixture.ConnectionString, docId, T0);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();

            var act = () => gje.UpdateDraftHeaderAsync(
                docId,
                new GeneralJournalEntryDraftHeaderUpdate(
                    JournalType: null,
                    ReasonCode: "X",
                    Memo: "X",
                    ExternalReference: null,
                    AutoReverse: true,
                    AutoReverseOnUtc: null),
                updatedBy: "u1",
                ct: CancellationToken.None);

            await act.Should().ThrowAsync<GeneralJournalEntryAutoReverseOnUtcRequiredException>()
                .WithMessage("Auto reverse date is required when Auto reverse is turned on.");
        }

        var (docAfter, headerAfter) = await LoadDocAndHeaderAsync(host, docId);
        docAfter.CreatedAtUtc.Should().Be(T0);
        docAfter.UpdatedAtUtc.Should().Be(T0, "failed operation must not touch audit timestamps");
        headerAfter.CreatedAtUtc.Should().Be(T0);
        headerAfter.UpdatedAtUtc.Should().Be(T0);
    }

    private static async Task BackdateTimestampsAsync(string connectionString, Guid documentId, DateTime tsUtc)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync(
            """
            UPDATE documents
            SET created_at_utc = @Ts, updated_at_utc = @Ts
            WHERE id = @Id;

            UPDATE doc_general_journal_entry
            SET created_at_utc = @Ts, updated_at_utc = @Ts
            WHERE document_id = @Id;
            """,
            new { Id = documentId, Ts = tsUtc });
    }

    private static async Task<(DocumentRecord doc, GeneralJournalEntryHeaderRecord header)> LoadDocAndHeaderAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryRepository>();

        var doc = await docs.GetAsync(documentId, CancellationToken.None);
        doc.Should().NotBeNull("document must exist");

        var header = await gje.GetHeaderAsync(documentId, CancellationToken.None);
        header.Should().NotBeNull("typed header must exist for GJE");

        return (doc!, header!);
    }

    private static async Task<(Guid cashId, Guid revenueId)> EnsureMinimalAccountsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        var cashId = await mgmt.CreateAsync(
            new CreateAccountRequest(
                Code: "1000",
                Name: "Cash",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true),
            CancellationToken.None);

        var revenueId = await mgmt.CreateAsync(
            new CreateAccountRequest(
                Code: "4000",
                Name: "Revenue",
                Type: AccountType.Income,
                StatementSection: StatementSection.Income,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true),
            CancellationToken.None);

        return (cashId, revenueId);
    }
}
