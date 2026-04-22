using System.Collections.Concurrent;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Documents;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.Documents.Workflow;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.GeneralJournalEntry;

/// <summary>
/// P0 (4-6): Numbering robustness at the document-type service level.
/// 4) CreateDraft fails on number uniqueness -> transaction rolls back (no sequence consumption)
/// 5) Concurrent Submit on same document -> exactly one succeeds (no deadlocks, single number)
/// 6) Approve after Submit -> must NOT consume the sequence again (number stable)
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GeneralJournalEntry_Numbering_Advanced_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string TypeCode = AccountingDocumentTypeCodes.GeneralJournalEntry;

    [Fact]
    public async Task CreateDraft_WhenNumberAlreadyTaken_Throws_UniqueViolation_AndRollsBack_NoSequenceConsumption()
    {
        using var host = CreateHost();

        var dateUtc = new DateTime(2026, 01, 15, 12, 0, 0, DateTimeKind.Utc);

        // Occupy the would-be first automatic number for the year.
        var reservedNumber = $"GJE-{dateUtc.Year}-000001";
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            await drafts.CreateDraftAsync(TypeCode, number: reservedNumber, dateUtc: dateUtc, manageTransaction: true, ct: CancellationToken.None);
        }

        Func<Task> act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            await gje.CreateDraftAsync(dateUtc, initiatedBy: "u_create", ct: CancellationToken.None);
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        ex.Which.ConstraintName.Should().Be("ux_documents_type_number_not_null");

        // Sequence row must not be consumed (transaction rollback).
        (await GetSequenceRowCountAsync(dateUtc.Year)).Should().Be(0);
    }

    [Fact]
    public async Task Submit_ConcurrentCallsOnSameDocument_ExactlyOneSucceeds_AndNumberAssignedOnce()
    {
        using var host = CreateHost();

        var (cashId, revenueId) = await EnsureMinimalAccountsAsync(host);
        var dateUtc = new DateTime(2026, 02, 10, 10, 0, 0, DateTimeKind.Utc);
        var docId = await CreateReadyDraftAsync(host, dateUtc, cashId, revenueId);

        var gate = new Barrier(2);
        var outcomes = new ConcurrentBag<Exception?>();

        Task RunAsync(string user) => Task.Run(async () =>
        {
            gate.SignalAndWait(TimeSpan.FromSeconds(10));
            try
            {
                await using var scope = host.Services.CreateAsyncScope();
                var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
                await gje.SubmitAsync(docId, submittedBy: user, ct: CancellationToken.None);
                outcomes.Add(null);
            }
            catch (Exception ex)
            {
                outcomes.Add(ex);
            }
        });

        await Task.WhenAll(RunAsync("u1"), RunAsync("u2"));

        outcomes.Should().HaveCount(2);
        outcomes.Count(x => x is null).Should().Be(1, "exactly one Submit should win");
        outcomes.Single(x => x is not null)!.Should().BeOfType<DocumentWorkflowStateMismatchException>()
            .Which.Message.Should().Contain("Expected Draft state, got");

        // Final state.
        var number = await GetDocumentNumberAsync(docId);
        number.Should().NotBeNullOrWhiteSpace();
        number.Should().Contain($"-{dateUtc.Year}-");

        var header = await GetHeaderAsync(docId);
        header.ApprovalState.Should().Be((short)GeneralJournalEntryModels.ApprovalState.Submitted);
        header.SubmittedBy.Should().BeOneOf("u1", "u2");
        header.SubmittedAtUtc.Should().NotBeNull();

        // Sequence must be consumed exactly once.
        (await GetSequenceLastSeqAsync(dateUtc.Year)).Should().Be(1);
    }

    [Fact]
    public async Task Approve_AfterSubmit_DoesNotConsumeSequenceAgain_NumberIsStable()
    {
        using var host = CreateHost();

        var (cashId, revenueId) = await EnsureMinimalAccountsAsync(host);
        var dateUtc = new DateTime(2026, 03, 05, 9, 30, 0, DateTimeKind.Utc);
        var docId = await CreateReadyDraftAsync(host, dateUtc, cashId, revenueId);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            await gje.SubmitAsync(docId, submittedBy: "u_submit", ct: CancellationToken.None);
        }

        var numberAfterSubmit = await GetDocumentNumberAsync(docId);
        numberAfterSubmit.Should().NotBeNullOrWhiteSpace();
        (await GetSequenceLastSeqAsync(dateUtc.Year)).Should().Be(1);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            await gje.ApproveAsync(docId, approvedBy: "u_approve", ct: CancellationToken.None);
        }

        var numberAfterApprove = await GetDocumentNumberAsync(docId);
        numberAfterApprove.Should().Be(numberAfterSubmit, "document number must be stable across approval transitions");

        // Critical: EnsureNumberAsync must short-circuit and NOT bump sequence.
        (await GetSequenceLastSeqAsync(dateUtc.Year)).Should().Be(1);

        var header = await GetHeaderAsync(docId);
        header.ApprovalState.Should().Be((short)GeneralJournalEntryModels.ApprovalState.Approved);
        header.ApprovedBy.Should().Be("u_approve");
        header.ApprovedAtUtc.Should().NotBeNull();
    }

    private IHost CreateHost() => IntegrationHostFactory.Create(Fixture.ConnectionString);

    private static async Task<(Guid cashId, Guid revenueId)> EnsureMinimalAccountsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        var cashId = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow,
            IsActive: true), CancellationToken.None);

        var revenueId = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow,
            IsActive: true), CancellationToken.None);

        return (cashId, revenueId);
    }

    private static async Task<Guid> CreateReadyDraftAsync(IHost host, DateTime dateUtc, Guid debitAccountId, Guid creditAccountId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();

        var id = await gje.CreateDraftAsync(dateUtc, initiatedBy: "u_init", ct: CancellationToken.None);

        await gje.UpdateDraftHeaderAsync(
            id,
            new GeneralJournalEntryDraftHeaderUpdate(
                JournalType: GeneralJournalEntryModels.JournalType.Adjusting,
                ReasonCode: "ADJ",
                Memo: "Integration test",
                ExternalReference: null,
                AutoReverse: false,
                AutoReverseOnUtc: null),
            updatedBy: "u_init",
            ct: CancellationToken.None);

        await gje.ReplaceDraftLinesAsync(
            id,
            new[]
            {
                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Debit,
                    AccountId: debitAccountId,
                    Amount: 100m,
                    Memo: "D"),
                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Credit,
                    AccountId: creditAccountId,
                    Amount: 100m,
                    Memo: "C"),
            },
            updatedBy: "u_init",
            ct: CancellationToken.None);

        return id;
    }

    private async Task<string?> GetDocumentNumberAsync(Guid documentId)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        const string sql = "SELECT number FROM documents WHERE id = @documentId;";
        return await conn.ExecuteScalarAsync<string?>(sql, new { documentId });
    }

    private sealed record HeaderSnapshot(
        short ApprovalState,
        string? SubmittedBy,
        DateTime? SubmittedAtUtc,
        string? ApprovedBy,
        DateTime? ApprovedAtUtc);

    private async Task<HeaderSnapshot> GetHeaderAsync(Guid documentId)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        const string sql = """
SELECT
    approval_state AS ApprovalState,
    submitted_by AS SubmittedBy,
    submitted_at_utc AS SubmittedAtUtc,
    approved_by AS ApprovedBy,
    approved_at_utc AS ApprovedAtUtc
FROM doc_general_journal_entry
WHERE document_id = @documentId;
""";

        var row = await conn.QuerySingleAsync<HeaderSnapshot>(sql, new { documentId });
        return row;
    }

    private async Task<int> GetSequenceRowCountAsync(int fiscalYear)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        const string sql = """
SELECT COUNT(*)
FROM document_number_sequences
WHERE type_code = @typeCode AND fiscal_year = @fiscalYear;
""";

        return await conn.ExecuteScalarAsync<int>(sql, new { typeCode = TypeCode, fiscalYear });
    }

    private async Task<long> GetSequenceLastSeqAsync(int fiscalYear)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        const string sql = """
SELECT last_seq
FROM document_number_sequences
WHERE type_code = @typeCode AND fiscal_year = @fiscalYear;
""";

        return await conn.ExecuteScalarAsync<long>(sql, new { typeCode = TypeCode, fiscalYear });
    }
}
