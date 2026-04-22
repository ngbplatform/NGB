using Dapper;
using FluentAssertions;
using NGB.Accounting.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// P1: Defense-in-depth for the General Journal Entry typed storages.
///
/// These tests intentionally bypass application validators and write directly to the typed tables,
/// asserting that DB CHECK/FK constraints prevent invalid platform states.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DatabaseConstraints_GeneralJournalEntry_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime T0 = new(2026, 1, 18, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Header_JournalTypeEnum_IsEnforcedByDb()
    {
        await using var conn = await OpenAsync();
        var docId = Guid.CreateVersion7();
        await InsertDocumentDraftAsync(conn, docId);

        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO doc_general_journal_entry(
                    document_id, journal_type, source, approval_state,
                    auto_reverse, auto_reverse_on_utc, reversal_of_document_id,
                    created_at_utc, updated_at_utc
                ) VALUES (
                    @DocumentId, 99, 1, 1,
                    FALSE, NULL, NULL,
                    @Now, @Now
                );
                """,
                new { DocumentId = docId, Now = T0 });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be("ck_doc_gje_journal_type");
    }

    [Fact]
    public async Task Header_SourceEnum_IsEnforcedByDb()
    {
        await using var conn = await OpenAsync();
        var docId = Guid.CreateVersion7();
        await InsertDocumentDraftAsync(conn, docId);

        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO doc_general_journal_entry(
                    document_id, journal_type, source, approval_state,
                    auto_reverse, auto_reverse_on_utc, reversal_of_document_id,
                    created_at_utc, updated_at_utc
                ) VALUES (
                    @DocumentId, 1, 99, 1,
                    FALSE, NULL, NULL,
                    @Now, @Now
                );
                """,
                new { DocumentId = docId, Now = T0 });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be("ck_doc_gje_source");
    }

    [Fact]
    public async Task Header_ApprovalStateEnum_IsEnforcedByDb()
    {
        await using var conn = await OpenAsync();
        var docId = Guid.CreateVersion7();
        await InsertDocumentDraftAsync(conn, docId);

        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO doc_general_journal_entry(
                    document_id, journal_type, source, approval_state,
                    auto_reverse, auto_reverse_on_utc, reversal_of_document_id,
                    created_at_utc, updated_at_utc
                ) VALUES (
                    @DocumentId, 1, 1, 99,
                    FALSE, NULL, NULL,
                    @Now, @Now
                );
                """,
                new { DocumentId = docId, Now = T0 });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be("ck_doc_gje_approval_state");
    }

    [Fact]
    public async Task Header_ReasonAndMemoRequired_WhenNotDraft_IsEnforcedByDb()
    {
        await using var conn = await OpenAsync();
        var docId = Guid.CreateVersion7();
        await InsertDocumentDraftAsync(conn, docId);

        // approval_state=2 must also satisfy submission audit gating.
        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO doc_general_journal_entry(
                    document_id, journal_type, source, approval_state,
                    reason_code, memo,
                    submitted_by, submitted_at_utc,
                    auto_reverse, auto_reverse_on_utc, reversal_of_document_id,
                    created_at_utc, updated_at_utc
                ) VALUES (
                    @DocumentId, 1, 1, 2,
                    NULL, 'memo',
                    'u', @Now,
                    FALSE, NULL, NULL,
                    @Now, @Now
                );
                """,
                new { DocumentId = docId, Now = T0 });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be("ck_doc_gje_reason_memo_required");
    }

    [Fact]
    public async Task Header_AutoReverseRequiresDate_IsEnforcedByDb()
    {
        await using var conn = await OpenAsync();
        var docId = Guid.CreateVersion7();
        await InsertDocumentDraftAsync(conn, docId);

        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO doc_general_journal_entry(
                    document_id, journal_type, source, approval_state,
                    auto_reverse, auto_reverse_on_utc, reversal_of_document_id,
                    created_at_utc, updated_at_utc
                ) VALUES (
                    @DocumentId, 1, 1, 1,
                    TRUE, NULL, NULL,
                    @Now, @Now
                );
                """,
                new { DocumentId = docId, Now = T0 });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be("ck_doc_gje_auto_reverse_fields");
    }

    [Fact]
    public async Task Header_ReversalRequiresSystemReversalShape_IsEnforcedByDb()
    {
        await using var conn = await OpenAsync();

        // Original document exists (FK to documents).
        var originalId = Guid.CreateVersion7();
        await InsertDocumentDraftAsync(conn, originalId);

        // Reversal document exists and points to original, but violates the required shape.
        var reversalId = Guid.CreateVersion7();
        await InsertDocumentDraftAsync(conn, reversalId);

        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO doc_general_journal_entry(
                    document_id,
                    journal_type, source, approval_state,
                    reason_code, memo,
                    approved_by, approved_at_utc,
                    auto_reverse, auto_reverse_on_utc,
                    reversal_of_document_id,
                    created_at_utc, updated_at_utc
                ) VALUES (
                    @DocumentId,
                    1, 2, 3,
                    'R', 'M',
                    'u', @Now,
                    FALSE, NULL,
                    @OriginalId,
                    @Now, @Now
                );
                """,
                new { DocumentId = reversalId, OriginalId = originalId, Now = T0 });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be("ck_doc_gje_reversal_doc");
    }

    [Fact]
    public async Task Header_SystemMustBeApproved_IsEnforcedByDb()
    {
        await using var conn = await OpenAsync();
        var docId = Guid.CreateVersion7();
        await InsertDocumentDraftAsync(conn, docId);

        // source=2 requires approval_state=3.
        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO doc_general_journal_entry(
                    document_id,
                    journal_type, source, approval_state,
                    reason_code, memo,
                    submitted_by, submitted_at_utc,
                    auto_reverse, auto_reverse_on_utc, reversal_of_document_id,
                    created_at_utc, updated_at_utc
                ) VALUES (
                    @DocumentId,
                    1, 2, 2,
                    'R', 'M',
                    'u', @Now,
                    FALSE, NULL, NULL,
                    @Now, @Now
                );
                """,
                new { DocumentId = docId, Now = T0 });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be("ck_doc_gje_system_is_approved");
    }

    [Fact]
    public async Task Header_SubmissionAuditColumns_AreGatedByApprovalState_IsEnforcedByDb()
    {
        await using var conn = await OpenAsync();
        var docId = Guid.CreateVersion7();
        await InsertDocumentDraftAsync(conn, docId);

        // approval_state=2 must have submitted_* populated.
        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO doc_general_journal_entry(
                    document_id,
                    journal_type, source, approval_state,
                    reason_code, memo,
                    submitted_by, submitted_at_utc,
                    auto_reverse, auto_reverse_on_utc, reversal_of_document_id,
                    created_at_utc, updated_at_utc
                ) VALUES (
                    @DocumentId,
                    1, 1, 2,
                    'R', 'M',
                    NULL, NULL,
                    FALSE, NULL, NULL,
                    @Now, @Now
                );
                """,
                new { DocumentId = docId, Now = T0 });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be("ck_doc_gje_submission_state");
    }

    [Fact]
    public async Task Lines_SideEnum_IsEnforcedByDb()
    {
        await using var conn = await OpenAsync();
        var docId = Guid.CreateVersion7();
        await InsertDocumentDraftAsync(conn, docId);
        await InsertGjeDraftHeaderAsync(conn, docId);

        var accountId = await EnsureAnyAccountIdAsync(conn);

        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO doc_general_journal_entry__lines(
                    document_id, line_no, side, account_id, amount, memo
                ) VALUES (
                    @DocumentId, 1, 99, @AccountId, 1.0000, NULL
                );
                """,
                new { DocumentId = docId, AccountId = accountId });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be("ck_doc_gje_lines_side");
    }

    [Fact]
    public async Task Lines_AmountPositive_IsEnforcedByDb()
    {
        await using var conn = await OpenAsync();
        var docId = Guid.CreateVersion7();
        await InsertDocumentDraftAsync(conn, docId);
        await InsertGjeDraftHeaderAsync(conn, docId);

        var accountId = await EnsureAnyAccountIdAsync(conn);

        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO doc_general_journal_entry__lines(
                    document_id, line_no, side, account_id, amount, memo
                ) VALUES (
                    @DocumentId, 1, 1, @AccountId, 0.0000, NULL
                );
                """,
                new { DocumentId = docId, AccountId = accountId });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be("ck_doc_gje_lines_amount");
    }

    [Fact]
    public async Task Allocations_AmountPositive_IsEnforcedByDb()
    {
        await using var conn = await OpenAsync();
        var docId = Guid.CreateVersion7();
        await InsertDocumentDraftAsync(conn, docId);
        await InsertGjeDraftHeaderAsync(conn, docId);

        var accountId = await EnsureAnyAccountIdAsync(conn);

        // Need existing debit & credit lines due to FK.
        await conn.ExecuteAsync(
            """
            INSERT INTO doc_general_journal_entry__lines(document_id, line_no, side, account_id, amount)
            VALUES
                (@DocumentId, 1, 1, @AccountId, 1.0000),
                (@DocumentId, 2, 2, @AccountId, 1.0000);
            """,
            new { DocumentId = docId, AccountId = accountId });

        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO doc_general_journal_entry__allocations(
                    document_id, entry_no, debit_line_no, credit_line_no, amount
                ) VALUES (
                    @DocumentId, 1, 1, 2, 0.0000
                );
                """,
                new { DocumentId = docId });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be("ck_doc_gje_alloc_amount");
    }

    [Fact]
    public async Task Header_DraftRow_IsAcceptedByDb()
    {
        await using var conn = await OpenAsync();
        var docId = Guid.CreateVersion7();
        await InsertDocumentDraftAsync(conn, docId);

        await InsertGjeDraftHeaderAsync(conn, docId);

        var row = await conn.QuerySingleAsync<(Guid DocumentId, short ApprovalState)>(
            "SELECT document_id AS DocumentId, approval_state AS ApprovalState FROM doc_general_journal_entry WHERE document_id=@Id;",
            new { Id = docId });

        row.DocumentId.Should().Be(docId);
        row.ApprovalState.Should().Be(1);
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    private static Task InsertDocumentDraftAsync(NpgsqlConnection conn, Guid id)
        => conn.ExecuteAsync(
            """
            INSERT INTO documents(
                id, type_code, number, date_utc,
                status, posted_at_utc, marked_for_deletion_at_utc,
                created_at_utc, updated_at_utc
            ) VALUES (
                @Id, @TypeCode, NULL, @DateUtc,
                1, NULL, NULL,
                @Now, @Now
            );
            """,
            new
            {
                Id = id,
                TypeCode = AccountingDocumentTypeCodes.GeneralJournalEntry,
                DateUtc = T0,
                Now = T0
            });

    private static Task InsertGjeDraftHeaderAsync(NpgsqlConnection conn, Guid id)
        => conn.ExecuteAsync(
            """
            INSERT INTO doc_general_journal_entry(
                document_id,
                journal_type, source, approval_state,
                auto_reverse, auto_reverse_on_utc, reversal_of_document_id,
                created_at_utc, updated_at_utc
            ) VALUES (
                @DocumentId,
                1, 1, 1,
                FALSE, NULL, NULL,
                @Now, @Now
            );
            """,
            new { DocumentId = id, Now = T0 });

    private static async Task<Guid> EnsureAnyAccountIdAsync(NpgsqlConnection conn)
    {
        var existing = await conn.ExecuteScalarAsync<Guid?>(
            "SELECT account_id FROM accounting_accounts WHERE is_deleted = FALSE LIMIT 1;");

        if (existing is not null)
            return existing.Value;

        // Make the test self-contained: create a minimal account row to satisfy FK.
        var id = Guid.CreateVersion7();
        await conn.ExecuteAsync(
            """
    INSERT INTO accounting_accounts(
        account_id,
        code,
        name,
        account_type,
        statement_section,
        is_contra,
        negative_balance_policy,
        is_active,
        is_deleted,
        created_at_utc,
        updated_at_utc)
    VALUES (
        @id,
        'IT_GJE_DB',
        'IT GJE DB',
        1,
        1,
        FALSE,
        0,
        TRUE,
        FALSE,
        @now,
        @now);
    """,
            new { id, now = T0 });

        return id;
    }
}
