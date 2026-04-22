using Dapper;
using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Accounting.Documents;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// P6: Defense in depth. Once a document is Posted, its typed storage must be immutable at the DB level.
///
/// Why:
/// - prevents "silent" corruption via accidental direct SQL updates
/// - enforces auditability: posted docs are immutable; change requires Unpost + edit + Repost
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostedDocumentImmutability_TypedStorages_GeneralJournalEntry_P6Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime T0 = new(2026, 1, 17, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Trigger_Blocks_Mutation_OnTypedHeader_WhenDocumentIsPosted()
    {
        var docId = Guid.CreateVersion7();
        await InsertPostedGjeAsync(docId);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var act = async () =>
            await conn.ExecuteAsync(
                "UPDATE doc_general_journal_entry SET memo = memo || '!' WHERE document_id = @Id;",
                new { Id = docId });

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("55000");
        ex.Which.MessageText.Should().Contain("posted").And.Contain(docId.ToString());
    }

    [Fact]
    public async Task Trigger_Blocks_InsertUpdateDelete_OnLines_WhenDocumentIsPosted()
    {
        var docId = Guid.CreateVersion7();
        await InsertPostedGjeAsync(docId);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        // UPDATE
        var update = async () =>
            await conn.ExecuteAsync(
                "UPDATE doc_general_journal_entry__lines SET amount = amount + 1 WHERE document_id = @Id AND line_no = 1;",
                new { Id = docId });

        (await update.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("55000");

        // INSERT
        var insert = async () =>
            await conn.ExecuteAsync(
                """
                INSERT INTO doc_general_journal_entry__lines(document_id, line_no, side, account_id, amount, memo)
                VALUES (@Id, 99, 1, @Acc, 1.0000, NULL);
                """,
                new { Id = docId, Acc = Guid.CreateVersion7() });

        (await insert.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("55000");

        // DELETE
        var delete = async () =>
            await conn.ExecuteAsync(
                "DELETE FROM doc_general_journal_entry__lines WHERE document_id = @Id AND line_no = 1;",
                new { Id = docId });

        (await delete.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("55000");

        // Allocations table is also typed storage: must be immutable for posted documents.
        var insertAlloc = async () =>
            await conn.ExecuteAsync(
                "INSERT INTO doc_general_journal_entry__allocations(document_id, entry_no, debit_line_no, credit_line_no, amount) VALUES (@Id, 1, 1, 2, 100.0000);",
                new { Id = docId });

        (await insertAlloc.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("55000");
    }

    [Fact]
    public async Task Trigger_Allows_TypedStorageMutation_WhenDocumentIsDraft()
    {
        var docId = Guid.CreateVersion7();
        await InsertDraftGjeAsync(docId);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync(
            "UPDATE doc_general_journal_entry__lines SET amount = amount + 1 WHERE document_id = @Id AND line_no = 1;",
            new { Id = docId });

        var amount = await conn.ExecuteScalarAsync<decimal>(
            "SELECT amount FROM doc_general_journal_entry__lines WHERE document_id = @Id AND line_no = 1;",
            new { Id = docId });

        amount.Should().Be(101.0000m);
    }

    private async Task InsertPostedGjeAsync(Guid documentId)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync(
            """
            INSERT INTO documents(
                id, type_code, number, date_utc,
                status, posted_at_utc, marked_for_deletion_at_utc,
                created_at_utc, updated_at_utc
            ) VALUES (
                @Id, @TypeCode, NULL, @DateUtc,
                1, NULL, NULL,
                @CreatedAt, @UpdatedAt
            );
            """,
            new
            {
                Id = documentId,
                TypeCode = AccountingDocumentTypeCodes.GeneralJournalEntry,
                DateUtc = T0,
                CreatedAt = T0,
                UpdatedAt = T0
            });

        await conn.ExecuteAsync(
            """
            INSERT INTO doc_general_journal_entry(
                document_id, journal_type, source, approval_state,
                reason_code, memo, external_reference,
                auto_reverse, auto_reverse_on_utc, reversal_of_document_id,
                initiated_by, initiated_at_utc,
                submitted_by, submitted_at_utc,
                approved_by, approved_at_utc,
                rejected_by, rejected_at_utc, reject_reason,
                posted_by, posted_at_utc,
                created_at_utc, updated_at_utc
            ) VALUES (
                @Id, @JournalType, @Source, @ApprovalState,
                NULL, NULL, NULL,
                FALSE, NULL, NULL,
                NULL, NULL,
                NULL, NULL,
                NULL, NULL,
                NULL, NULL, NULL,
                NULL, NULL,
                @Now, @Now
            );
            """,
            new
            {
                Id = documentId,
                JournalType = (short)GeneralJournalEntryModels.JournalType.Standard,
                Source = (short)GeneralJournalEntryModels.Source.Manual,
                ApprovalState = (short)GeneralJournalEntryModels.ApprovalState.Draft,
                Now = T0
            });

        await InsertTwoBalancedLinesAsync(conn, documentId);

        // Transition to Approved after typed storage is populated.
        // Manual GJE typed lines are mutable only in Draft state.
        await conn.ExecuteAsync(
            """
            UPDATE doc_general_journal_entry
               SET approval_state = @Approved,
                   reason_code = 'ADJ',
                   memo = 'Posted',
                   submitted_by = 'U',
                   submitted_at_utc = @Now,
                   approved_by = 'U',
                   approved_at_utc = @Now,
                   updated_at_utc = @Now
             WHERE document_id = @Id;
            """,
            new { Id = documentId, Approved = (short)GeneralJournalEntryModels.ApprovalState.Approved, Now = T0 });

        // Set posted audit on typed header BEFORE flipping the common document status to Posted.
        // The posted immutability trigger checks documents.status, so we must update typed header while the document is still not Posted.
        await conn.ExecuteAsync(
            """
            UPDATE doc_general_journal_entry
               SET posted_by = 'U', posted_at_utc = @PostedAt, updated_at_utc = @PostedAt
             WHERE document_id = @Id;
            """,
            new { Id = documentId, PostedAt = T0 });

        await conn.ExecuteAsync(
            """
            UPDATE documents
               SET status = 2, posted_at_utc = @PostedAt, updated_at_utc = @PostedAt
             WHERE id = @Id;
            """,
            new { Id = documentId, PostedAt = T0 });
    }

    private async Task InsertDraftGjeAsync(Guid documentId)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync(
            """
            INSERT INTO documents(
                id, type_code, number, date_utc,
                status, posted_at_utc, marked_for_deletion_at_utc,
                created_at_utc, updated_at_utc
            ) VALUES (
                @Id, @TypeCode, NULL, @DateUtc,
                1, NULL, NULL,
                @CreatedAt, @UpdatedAt
            );
            """,
            new
            {
                Id = documentId,
                TypeCode = AccountingDocumentTypeCodes.GeneralJournalEntry,
                DateUtc = T0,
                CreatedAt = T0,
                UpdatedAt = T0
            });

        await conn.ExecuteAsync(
            """
            INSERT INTO doc_general_journal_entry(
                document_id, journal_type, source, approval_state,
                reason_code, memo, external_reference,
                auto_reverse, auto_reverse_on_utc, reversal_of_document_id,
                initiated_by, initiated_at_utc,
                submitted_by, submitted_at_utc,
                approved_by, approved_at_utc,
                rejected_by, rejected_at_utc, reject_reason,
                posted_by, posted_at_utc,
                created_at_utc, updated_at_utc
            ) VALUES (
                @Id, @JournalType, @Source, @ApprovalState,
                NULL, NULL, NULL,
                FALSE, NULL, NULL,
                NULL, NULL,
                NULL, NULL,
                NULL, NULL,
                NULL, NULL, NULL,
                NULL, NULL,
                @Now, @Now
            );
            """,
            new
            {
                Id = documentId,
                JournalType = (short)GeneralJournalEntryModels.JournalType.Standard,
                Source = (short)GeneralJournalEntryModels.Source.Manual,
                ApprovalState = (short)GeneralJournalEntryModels.ApprovalState.Draft,
                Now = T0
            });

        await InsertTwoBalancedLinesAsync(conn, documentId);
    }

    private static async Task InsertTwoBalancedLinesAsync(NpgsqlConnection conn, Guid documentId)
    {
        // The typed lines table enforces FK to accounting_accounts(account_id).
        // Tests must create real accounts to avoid false failures unrelated to the immutability guard.
        var acc1 = Guid.CreateVersion7();
        var acc2 = Guid.CreateVersion7();

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
                @Acc1,
                'IT_GJE_A',
                'IT GJE A',
                @Asset,
                @Assets,
                FALSE,
                @Allow,
                TRUE,
                FALSE,
                @Now,
                @Now);

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
                @Acc2,
                'IT_GJE_B',
                'IT GJE B',
                @Liability,
                @Liabilities,
                FALSE,
                @Allow,
                TRUE,
                FALSE,
                @Now,
                @Now);
            """,
            new
            {
                Acc1 = acc1,
                Acc2 = acc2,
                Asset = (short)AccountType.Asset,
                Liability = (short)AccountType.Liability,
                Assets = (short)StatementSection.Assets,
                Liabilities = (short)StatementSection.Liabilities,
                Allow = (short)NegativeBalancePolicy.Allow,
                Now = T0
            });

        await conn.ExecuteAsync(
            """
            INSERT INTO doc_general_journal_entry__lines(document_id, line_no, side, account_id, amount, memo)
            VALUES (@Id, 1, 1, @Acc1, 100.0000, NULL);

            INSERT INTO doc_general_journal_entry__lines(document_id, line_no, side, account_id, amount, memo)
            VALUES (@Id, 2, 2, @Acc2, 100.0000, NULL);
            """,
            new { Id = documentId, Acc1 = acc1, Acc2 = acc2 });
    }
}
