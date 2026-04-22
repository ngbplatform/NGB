using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.GeneralJournalEntry;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.Documents.GeneralJournalEntry.Exceptions;
using NGB.Runtime.Documents.Workflow;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Extensions;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.GeneralJournalEntry;

/// <summary>
/// P0: General Journal Entry (GJE) workflow/state machine must be regression-proof.
/// Covers forbidden transitions and verifies "no side effects" on failure.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GeneralJournalEntry_StateMachine_P0Tests(PostgresTestFixture fixture)
{
    private PostgresTestFixture Fixture { get; } = fixture;

    [Fact]
    public async Task Submit_WithoutReasonCodeOrMemo_Throws_AndStateStaysDraft()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var docDateUtc = new DateTime(2026, 01, 10, 12, 0, 0, DateTimeKind.Utc);
        Guid docId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            docId = await gje.CreateDraftAsync(docDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

            await FluentActions.Awaiting(() => gje.SubmitAsync(docId, submittedBy: "u1", ct: CancellationToken.None))
                .Should().ThrowAsync<GeneralJournalEntryBusinessFieldRequiredException>()
                .WithMessage("Reason code is required.");
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var gjeRepo = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryRepository>();

            var doc = await docs.GetAsync(docId, CancellationToken.None);
            doc.Should().NotBeNull();
            doc!.Status.Should().Be(DocumentStatus.Draft);
            doc.Number.Should().NotBeNullOrWhiteSpace("draft numbering now happens on create and must survive failed Submit");

            var header = await gjeRepo.GetHeaderAsync(docId, CancellationToken.None);
            header.Should().NotBeNull();
            header!.ApprovalState.Should().Be(GeneralJournalEntryModels.ApprovalState.Draft);
            header.SubmittedBy.Should().BeNull();
            header.SubmittedAtUtc.Should().BeNull();
        }
    }

    [Fact]
    public async Task Submit_WithBusinessFields_ButUnbalancedLines_Throws_AndStateStaysDraft()
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
                    ReasonCode: "TEST",
                    Memo: "Unbalanced",
                    ExternalReference: null,
                    AutoReverse: false,
                    AutoReverseOnUtc: null),
                updatedBy: "u1",
                ct: CancellationToken.None);

            // Intentionally unbalanced: 100 debit vs 90 credit.
            await gje.ReplaceDraftLinesAsync(
                docId,
                new[]
                {
                    new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Debit, cashId, 100m, null),
                    new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Credit, revenueId, 90m, null),
                },
                updatedBy: "u1",
                ct: CancellationToken.None);

            await FluentActions.Awaiting(() => gje.SubmitAsync(docId, submittedBy: "u1", ct: CancellationToken.None))
                .Should().ThrowAsync<GeneralJournalEntryUnbalancedLinesException>()
                .WithMessage("Journal entry is not balanced. Debit * vs Credit *");
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var gjeRepo = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryRepository>();

            var doc = await docs.GetAsync(docId, CancellationToken.None);
            doc.Should().NotBeNull();
            doc!.Status.Should().Be(DocumentStatus.Draft);
            doc.Number.Should().NotBeNullOrWhiteSpace("draft numbering now happens on create and must survive failed Submit");

            var header = await gjeRepo.GetHeaderAsync(docId, CancellationToken.None);
            header.Should().NotBeNull();
            header!.ApprovalState.Should().Be(GeneralJournalEntryModels.ApprovalState.Draft);
            header.SubmittedBy.Should().BeNull();
            header.SubmittedAtUtc.Should().BeNull();
        }
    }

    [Fact]
    public async Task Submit_WhenAlreadySubmitted_Throws_AndDoesNotChangeSubmittedAudit()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId) = await EnsureMinimalAccountsAsync(host);

        var docDateUtc = new DateTime(2026, 01, 10, 12, 0, 0, DateTimeKind.Utc);
        Guid docId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            var gjeRepo = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryRepository>();

            docId = await gje.CreateDraftAsync(docDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

            await gje.UpdateDraftHeaderAsync(
                docId,
                new GeneralJournalEntryDraftHeaderUpdate(null, "TEST", "Submit twice", null, false, null),
                updatedBy: "u1",
                ct: CancellationToken.None);

            await gje.ReplaceDraftLinesAsync(
                docId,
                new[]
                {
                    new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Debit, cashId, 10m, null),
                    new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Credit, revenueId, 10m, null),
                },
                updatedBy: "u1",
                ct: CancellationToken.None);

            await gje.SubmitAsync(docId, submittedBy: "u1", ct: CancellationToken.None);

            var before = await gjeRepo.GetHeaderAsync(docId, CancellationToken.None);
            before.Should().NotBeNull();
            var submittedAt = before!.SubmittedAtUtc;
            submittedAt.Should().NotBeNull();

            await FluentActions.Awaiting(() => gje.SubmitAsync(docId, submittedBy: "u1", ct: CancellationToken.None))
                .Should().ThrowAsync<DocumentWorkflowStateMismatchException>()
                .WithMessage("Expected Draft state, got Submitted.*");

            var after = await gjeRepo.GetHeaderAsync(docId, CancellationToken.None);
            after.Should().NotBeNull();
            after!.ApprovalState.Should().Be(GeneralJournalEntryModels.ApprovalState.Submitted);
            after.SubmittedAtUtc.Should().Be(submittedAt, "failed re-submit must not mutate audit fields");
            after.SubmittedBy.Should().Be("u1");
        }
    }

    [Fact]
    public async Task Approve_WhenNotSubmitted_Throws()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var docDateUtc = new DateTime(2026, 01, 10, 12, 0, 0, DateTimeKind.Utc);
        Guid docId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            docId = await gje.CreateDraftAsync(docDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

            await FluentActions.Awaiting(() => gje.ApproveAsync(docId, approvedBy: "u2", ct: CancellationToken.None))
                .Should().ThrowAsync<DocumentWorkflowStateMismatchException>()
                .WithMessage("Expected Submitted state, got Draft.*");
        }
    }

    [Fact]
    public async Task Reject_WhenNotSubmitted_Throws()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var docDateUtc = new DateTime(2026, 01, 10, 12, 0, 0, DateTimeKind.Utc);
        Guid docId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            docId = await gje.CreateDraftAsync(docDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

            await FluentActions.Awaiting(() => gje.RejectAsync(docId, rejectedBy: "u2", rejectReason: "no", ct: CancellationToken.None))
                .Should().ThrowAsync<DocumentWorkflowStateMismatchException>()
                .WithMessage("Expected Submitted state, got Draft.*");
        }
    }

    [Fact]
    public async Task Reject_WithEmptyReason_ThrowsArgumentRequiredException()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var docDateUtc = new DateTime(2026, 01, 10, 12, 0, 0, DateTimeKind.Utc);

        await using var scope = host.Services.CreateAsyncScope();
        var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
        var docId = await gje.CreateDraftAsync(docDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

        var ex = await FluentActions.Awaiting(() => gje.RejectAsync(docId, rejectedBy: "u2", rejectReason: " ", ct: CancellationToken.None))
            .Should().ThrowAsync<NgbArgumentRequiredException>();

        ex.Which.ParamName.Should().Be("rejectReason");
        ex.Which.AssertNgbError(NgbArgumentRequiredException.Code, "paramName");
    }

    [Fact]
    public async Task PostApproved_WhenNotApproved_Throws_AndDoesNotWriteRegister()
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

            await gje.UpdateDraftHeaderAsync(docId, new GeneralJournalEntryDraftHeaderUpdate(null, "TEST", "Not approved", null, false, null), "u1", CancellationToken.None);

            await gje.ReplaceDraftLinesAsync(
                docId,
                new[]
                {
                    new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Debit, cashId, 10m, null),
                    new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Credit, revenueId, 10m, null),
                },
                updatedBy: "u1",
                ct: CancellationToken.None);

            await gje.SubmitAsync(docId, submittedBy: "u1", ct: CancellationToken.None);

            var ex = await FluentActions.Awaiting(() => gje.PostApprovedAsync(docId, postedBy: "u2", ct: CancellationToken.None))
                .Should().ThrowAsync<DocumentWorkflowStateMismatchException>();

            ex.Which.ErrorCode.Should().Be(DocumentWorkflowStateMismatchException.ErrorCodeConst);
            ex.Which.Context.Should().ContainKey("expectedState").WhoseValue.Should().Be(GeneralJournalEntryModels.ApprovalState.Approved.ToString());
        }

        await AssertNoRegisterWritesAsync(docId);
    }

    [Fact]
    public async Task PostApproved_WhenMarkedForDeletion_Throws_AndDoesNotWriteRegister()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId) = await EnsureMinimalAccountsAsync(host);
        var docDateUtc = new DateTime(2026, 01, 10, 12, 0, 0, DateTimeKind.Utc);
        Guid docId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            docId = await gje.CreateDraftAsync(docDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

            await gje.UpdateDraftHeaderAsync(docId, new GeneralJournalEntryDraftHeaderUpdate(null, "TEST", "Delete me", null, false, null), "u1", CancellationToken.None);
            await gje.ReplaceDraftLinesAsync(
                docId,
                new[]
                {
                    new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Debit, cashId, 10m, null),
                    new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Credit, revenueId, 10m, null),
                },
                "u1",
                CancellationToken.None);

            await gje.SubmitAsync(docId, submittedBy: "u1", ct: CancellationToken.None);
            await gje.ApproveAsync(docId, approvedBy: "u2", ct: CancellationToken.None);

            await posting.MarkForDeletionAsync(docId, CancellationToken.None);

            var ex = await FluentActions.Awaiting(() => gje.PostApprovedAsync(docId, postedBy: "u2", ct: CancellationToken.None))
                .Should().ThrowAsync<DocumentMarkedForDeletionException>();

            ex.Which.ErrorCode.Should().Be(DocumentMarkedForDeletionException.ErrorCodeConst);
            ex.Which.Context.Should().ContainKey("operation").WhoseValue.Should().Be(GeneralJournalEntryWorkflowOperationNames.PostApproved);
        }

        await AssertNoRegisterWritesAsync(docId);
    }

    [Fact]
    public async Task SystemGje_IsImmutable_AndCannotBeSubmittedOrEdited()
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
                new GeneralJournalEntryDraftHeaderUpdate(null, "TEST", "Auto reverse", null, true, reverseOn),
                updatedBy: "u1",
                ct: CancellationToken.None);

            await gje.ReplaceDraftLinesAsync(
                originalId,
                new[]
                {
                    new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Debit, cashId, 25m, null),
                    new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Credit, revenueId, 25m, null),
                },
                updatedBy: "u1",
                ct: CancellationToken.None);

            await gje.SubmitAsync(originalId, submittedBy: "u1", ct: CancellationToken.None);
            await gje.ApproveAsync(originalId, approvedBy: "u2", ct: CancellationToken.None);
            await gje.PostApprovedAsync(originalId, postedBy: "u2", ct: CancellationToken.None);
        }

        var systemReversalId = DeterministicGuid.Create($"gje:auto-reversal:{originalId:N}:{reverseOn:yyyy-MM-dd}");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();

            var ex1 = await FluentActions.Awaiting(() => gje.UpdateDraftHeaderAsync(systemReversalId, new GeneralJournalEntryDraftHeaderUpdate(null, "X", "Y", null, false, null), "u1", CancellationToken.None))
                .Should().ThrowAsync<GeneralJournalEntrySystemDocumentOperationForbiddenException>();

            ex1.Which.ErrorCode.Should().Be(GeneralJournalEntrySystemDocumentOperationForbiddenException.ErrorCodeConst);

            var ex2 = await FluentActions.Awaiting(() => gje.ReplaceDraftLinesAsync(systemReversalId, Array.Empty<GeneralJournalEntryDraftLineInput>(), "u1", CancellationToken.None))
                .Should().ThrowAsync<GeneralJournalEntrySystemDocumentOperationForbiddenException>();

            ex2.Which.ErrorCode.Should().Be(GeneralJournalEntrySystemDocumentOperationForbiddenException.ErrorCodeConst);

            var ex3 = await FluentActions.Awaiting(() => gje.SubmitAsync(systemReversalId, submittedBy: "u1", ct: CancellationToken.None))
                .Should().ThrowAsync<GeneralJournalEntrySystemDocumentOperationForbiddenException>();

            ex3.Which.ErrorCode.Should().Be(GeneralJournalEntrySystemDocumentOperationForbiddenException.ErrorCodeConst);
        }
    }

    [Fact]
    public async Task PostApproved_WhenAlreadyPosted_IsNoOp_AndDoesNotDuplicateRegister()
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

            await gje.UpdateDraftHeaderAsync(docId, new GeneralJournalEntryDraftHeaderUpdate(null, "TEST", "Post twice", null, false, null), "u1", CancellationToken.None);
            await gje.ReplaceDraftLinesAsync(
                docId,
                new[]
                {
                    new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Debit, cashId, 10m, null),
                    new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Credit, revenueId, 10m, null),
                },
                "u1",
                CancellationToken.None);

            await gje.SubmitAsync(docId, submittedBy: "u1", ct: CancellationToken.None);
            await gje.ApproveAsync(docId, approvedBy: "u2", ct: CancellationToken.None);
            await gje.PostApprovedAsync(docId, postedBy: "u2", ct: CancellationToken.None);
        }

        var before = await GetRegisterCountAsync(docId);
        before.Should().Be(1);

        // second call must not throw and must not add register rows
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            await gje.PostApprovedAsync(docId, postedBy: "u2", ct: CancellationToken.None);
        }

        var after = await GetRegisterCountAsync(docId);
        after.Should().Be(1, "PostApproved on already-posted document must be a no-op");
    }

    private async Task AssertNoRegisterWritesAsync(Guid documentId)
    {
        (await GetRegisterCountAsync(documentId)).Should().Be(0);
        (await GetAllocationCountAsync(documentId)).Should().Be(0);
    }

    private async Task<int> GetRegisterCountAsync(Guid documentId)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();
        return (int)(await new NpgsqlCommand(
            "SELECT COUNT(*)::int FROM accounting_register_main WHERE document_id = @d",
            conn)
        {
            Parameters = { new("d", documentId) }
        }.ExecuteScalarAsync())!;
    }

    private async Task<int> GetAllocationCountAsync(Guid documentId)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();
        return (int)(await new NpgsqlCommand(
            "SELECT COUNT(*)::int FROM doc_general_journal_entry__allocations WHERE document_id = @d",
            conn)
        {
            Parameters = { new("d", documentId) }
        }.ExecuteScalarAsync())!;
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
