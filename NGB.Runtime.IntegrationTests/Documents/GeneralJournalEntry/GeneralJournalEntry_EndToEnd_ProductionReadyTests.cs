using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Documents;
using NGB.Core.Documents;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Persistence.Documents;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.Documents.Workflow;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.GeneralJournalEntry;

[Collection(PostgresCollection.Name)]
public sealed class GeneralJournalEntry_EndToEnd_ProductionReadyTests(PostgresTestFixture fixture)
{
    private PostgresTestFixture Fixture { get; } = fixture;

    [Fact]
    public async Task ManualGje_SubmitApprovePost_WritesRegisterAndAllocations_AndLocksDownEdits()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId) = await EnsureMinimalAccountsAsync(host);

        var docDateUtc = new DateTime(2026, 01, 10, 12, 0, 0, DateTimeKind.Utc);

        Guid docId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            docId = await gje.CreateDraftAsync(docDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

            await gje.UpdateDraftHeaderAsync(
                docId,
                new GeneralJournalEntryDraftHeaderUpdate(
                    JournalType: null,
                    ReasonCode: "ACCRUAL_FIX",
                    Memo: "Accrual adjustment",
                    ExternalReference: "EXT-1",
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
                        Memo: "Debit cash"),
                    new GeneralJournalEntryDraftLineInput(
                        Side: GeneralJournalEntryModels.LineSide.Credit,
                        AccountId: revenueId,
                        Amount: 100m,
                        Memo: "Credit revenue"),
                },
                updatedBy: "u1",
                ct: CancellationToken.None);

            await gje.SubmitAsync(docId, submittedBy: "u1", ct: CancellationToken.None);

            await gje.ApproveAsync(docId, approvedBy: "u1", ct: CancellationToken.None);
            await gje.PostApprovedAsync(docId, postedBy: "u2", ct: CancellationToken.None);

            // Posted doc becomes immutable
            var ex = await FluentActions.Awaiting(() => gje.ReplaceDraftLinesAsync(docId, Array.Empty<GeneralJournalEntryDraftLineInput>(), "u2", CancellationToken.None))
                .Should().ThrowAsync<DocumentWorkflowStateMismatchException>();

            ex.Which.ErrorCode.Should().Be(DocumentWorkflowStateMismatchException.ErrorCodeConst);
            ex.Which.Context.Should().ContainKey("expectedState").WhoseValue.Should().Be(DocumentStatus.Draft.ToString());
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var doc = await docs.GetAsync(docId, CancellationToken.None);
            doc.Should().NotBeNull();
            doc!.Status.Should().Be(DocumentStatus.Posted);

            // Allocations stored 1:1 for the simple case
            await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
            await conn.OpenAsync();

            var regCount = (int)(await new NpgsqlCommand(
                "SELECT COUNT(*)::int FROM accounting_register_main WHERE document_id = @d",
                conn)
            {
                Parameters = { new("d", docId) }
            }.ExecuteScalarAsync())!;

            var allocCount = (int)(await new NpgsqlCommand(
                "SELECT COUNT(*)::int FROM doc_general_journal_entry__allocations WHERE document_id = @d",
                conn)
            {
                Parameters = { new("d", docId) }
            }.ExecuteScalarAsync())!;

            regCount.Should().Be(1);
            allocCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task AutoReverse_CreatesSystemReversal_AndRunnerPostsOnlyWhenDue()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId) = await EnsureMinimalAccountsAsync(host);

        var docDateUtc = new DateTime(2026, 01, 10, 12, 0, 0, DateTimeKind.Utc);
        var reverseOn = new DateOnly(2026, 01, 20);

        Guid originalId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            originalId = await gje.CreateDraftAsync(docDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

            await gje.UpdateDraftHeaderAsync(
                originalId,
                new GeneralJournalEntryDraftHeaderUpdate(
                    JournalType: null,
                    ReasonCode: "ACCRUAL",
                    Memo: "Accrual with auto reverse",
                    ExternalReference: null,
                    AutoReverse: true,
                    AutoReverseOnUtc: reverseOn),
                updatedBy: "u1",
                ct: CancellationToken.None);

            await gje.ReplaceDraftLinesAsync(
                originalId,
                new[]
                {
                    new GeneralJournalEntryDraftLineInput(
                        Side: GeneralJournalEntryModels.LineSide.Debit,
                        AccountId: cashId,
                        Amount: 25m,
                        Memo: null),
                    new GeneralJournalEntryDraftLineInput(
                        Side: GeneralJournalEntryModels.LineSide.Credit,
                        AccountId: revenueId,
                        Amount: 25m,
                        Memo: null),
                },
                updatedBy: "u1",
                ct: CancellationToken.None);

            await gje.SubmitAsync(originalId, submittedBy: "u1", ct: CancellationToken.None);
            await gje.ApproveAsync(originalId, approvedBy: "u2", ct: CancellationToken.None);
            await gje.PostApprovedAsync(originalId, postedBy: "u2", ct: CancellationToken.None);
        }

        var expectedReversalId = NGB.Tools.Extensions.DeterministicGuid.Create($"gje:auto-reversal:{originalId:N}:{reverseOn:yyyy-MM-dd}");

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var exists = (int)(await new NpgsqlCommand(
                "SELECT COUNT(*)::int FROM documents WHERE id = @d AND type_code = @t",
                conn)
            {
                Parameters =
                {
                    new("d", expectedReversalId),
                    new("t", AccountingDocumentTypeCodes.GeneralJournalEntry)
                }
            }.ExecuteScalarAsync())!;

            exists.Should().Be(1, "posting with AutoReverse must create a scheduled reversal document");
        }

        // Runner does NOT post early
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntrySystemReversalRunner>();
            (await runner.PostDueSystemReversalsAsync(new DateOnly(2026, 01, 19), batchSize: 50, postedBy: "SYSTEM", ct: CancellationToken.None))
                .Should().Be(0);
        }

        // Runner posts on due date
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntrySystemReversalRunner>();
            (await runner.PostDueSystemReversalsAsync(reverseOn, batchSize: 50, postedBy: "SYSTEM", ct: CancellationToken.None))
                .Should().Be(1);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var rev = await docs.GetAsync(expectedReversalId, CancellationToken.None);
            rev.Should().NotBeNull();
            rev!.Status.Should().Be(DocumentStatus.Posted);

            await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
            await conn.OpenAsync();

            var regCount = (int)(await new NpgsqlCommand(
                "SELECT COUNT(*)::int FROM accounting_register_main WHERE document_id = @d",
                conn)
            {
                Parameters = { new("d", expectedReversalId) }
            }.ExecuteScalarAsync())!;

            regCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task ReversePosted_IsIdempotent_AndPostsReversalImmediately_WhenRequested()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId) = await EnsureMinimalAccountsAsync(host);

        var docDateUtc = new DateTime(2026, 01, 10, 12, 0, 0, DateTimeKind.Utc);
        Guid originalId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            originalId = await gje.CreateDraftAsync(docDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

            await gje.UpdateDraftHeaderAsync(
                originalId,
                new GeneralJournalEntryDraftHeaderUpdate(
                    JournalType: null,
                    ReasonCode: "CORRECTION",
                    Memo: "Needs reversal",
                    ExternalReference: null,
                    AutoReverse: false,
                    AutoReverseOnUtc: null),
                updatedBy: "u1",
                ct: CancellationToken.None);

            await gje.ReplaceDraftLinesAsync(
                originalId,
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

            await gje.SubmitAsync(originalId, submittedBy: "u1", ct: CancellationToken.None);
            await gje.ApproveAsync(originalId, approvedBy: "u2", ct: CancellationToken.None);
            await gje.PostApprovedAsync(originalId, postedBy: "u2", ct: CancellationToken.None);

            var reversalDateUtc = new DateTime(2026, 01, 11, 0, 0, 0, DateTimeKind.Utc);
            var reversalId1 = await gje.ReversePostedAsync(originalId, reversalDateUtc, initiatedBy: "u3", postImmediately: true, ct: CancellationToken.None);
            var reversalId2 = await gje.ReversePostedAsync(originalId, reversalDateUtc, initiatedBy: "u3", postImmediately: true, ct: CancellationToken.None);

            reversalId2.Should().Be(reversalId1, "ReversePostedAsync must be idempotent");
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var reversalId = (Guid)(await new NpgsqlCommand(
                "SELECT id FROM documents WHERE type_code = @t AND id IN (SELECT document_id FROM doc_general_journal_entry WHERE reversal_of_document_id = @o)",
                conn)
            {
                Parameters =
                {
                    new("t", AccountingDocumentTypeCodes.GeneralJournalEntry),
                    new("o", originalId)
                }
            }.ExecuteScalarAsync())!;

            var regCount = (int)(await new NpgsqlCommand(
                "SELECT COUNT(*)::int FROM accounting_register_main WHERE document_id = @d",
                conn)
            {
                Parameters = { new("d", reversalId) }
            }.ExecuteScalarAsync())!;

            regCount.Should().Be(1);
        }
    }

    private static async Task<(Guid cashId, Guid revenueId)> EnsureMinimalAccountsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        // The fixture resets the DB at the beginning of each test, so we can create deterministic codes.
        // (IDs are generated by the service; we return them to the caller.)
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
