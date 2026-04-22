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
/// P1: Defense in depth.
/// Manual GJE becomes immutable (business fields + lines) once it leaves Draft approval state.
///
/// Why:
/// - app-level guards are necessary but not sufficient (direct SQL is always possible)
/// - auditability: submitted/approved/rejected content must not be silently changed
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ManualGeneralJournalEntryImmutabilityAfterSubmit_DatabaseGuards_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime T0 = new(2026, 1, 18, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Trigger_Blocks_HeaderBusinessFieldMutation_WhenSubmitted_Manual()
    {
        var docId = Guid.CreateVersion7();
        await InsertSubmittedManualGjeAsync(docId);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var act = async () =>
            await conn.ExecuteAsync(
                "UPDATE doc_general_journal_entry SET memo = memo || '!' WHERE document_id = @Id;",
                new { Id = docId });

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("55000");
        ex.Which.MessageText.Should().Contain("immutable").And.Contain(docId.ToString());
    }

    [Fact]
    public async Task Trigger_Blocks_Lines_InsertUpdateDelete_WhenSubmitted_Manual()
    {
        var docId = Guid.CreateVersion7();
        await InsertSubmittedManualGjeAsync(docId);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var update = async () =>
            await conn.ExecuteAsync(
                "UPDATE doc_general_journal_entry__lines SET amount = amount + 1 WHERE document_id = @Id AND line_no = 1;",
                new { Id = docId });

        (await update.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("55000");

        var insert = async () =>
            await conn.ExecuteAsync(
                """
                INSERT INTO doc_general_journal_entry__lines(document_id, line_no, side, account_id, amount, memo)
                VALUES (@Id, 99, 1, @Acc, 1.0000, NULL);
                """,
                new { Id = docId, Acc = Guid.CreateVersion7() });

        (await insert.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("55000");

        var delete = async () =>
            await conn.ExecuteAsync(
                "DELETE FROM doc_general_journal_entry__lines WHERE document_id = @Id AND line_no = 1;",
                new { Id = docId });

        (await delete.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("55000");
    }

    [Fact]
    public async Task Trigger_Blocks_Allocations_WhenSubmitted_But_Allows_WhenApproved_Manual()
    {
        var docId = Guid.CreateVersion7();
        await InsertSubmittedManualGjeAsync(docId);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var insertWhileSubmitted = async () =>
            await conn.ExecuteAsync(
                "INSERT INTO doc_general_journal_entry__allocations(document_id, entry_no, debit_line_no, credit_line_no, amount) VALUES (@Id, 1, 1, 2, 100.0000);",
                new { Id = docId });

        (await insertWhileSubmitted.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("55000");

        // Approve via SQL: this should be allowed (non-business audit transition).
        await conn.ExecuteAsync(
            """
            UPDATE doc_general_journal_entry
               SET approval_state = 3,
                   approved_by = 'u2',
                   approved_at_utc = @Now,
                   updated_at_utc = @Now
             WHERE document_id = @Id;
            """,
            new { Id = docId, Now = T0.AddMinutes(2) });

        await conn.ExecuteAsync(
            "INSERT INTO doc_general_journal_entry__allocations(document_id, entry_no, debit_line_no, credit_line_no, amount) VALUES (@Id, 1, 1, 2, 100.0000);",
            new { Id = docId });

        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM doc_general_journal_entry__allocations WHERE document_id = @Id;",
            new { Id = docId });

        count.Should().Be(1);
    }

    private async Task InsertSubmittedManualGjeAsync(Guid documentId)
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
                @Now, @Now
            );
            """,
            new
            {
                Id = documentId,
                TypeCode = AccountingDocumentTypeCodes.GeneralJournalEntry,
                DateUtc = T0,
                Now = T0
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

        // Move to Submitted state (still editable at DB level because OLD state was Draft).
        await conn.ExecuteAsync(
            """
            UPDATE doc_general_journal_entry
               SET reason_code = 'RC1',
                   memo = 'Submitted memo',
                   approval_state = 2,
                   submitted_by = 'u1',
                   submitted_at_utc = @Now,
                   updated_at_utc = @Now
             WHERE document_id = @Id;
            """,
            new { Id = documentId, Now = T0.AddMinutes(1) });
    }

    private static async Task InsertTwoBalancedLinesAsync(NpgsqlConnection conn, Guid documentId)
    {
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
