using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.Documents.Workflow;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.GeneralJournalEntry;

/// <summary>
/// Platform invariant:
/// - Once a manual GJE is submitted/approved/rejected, its business content must be immutable.
///   Otherwise you can change amounts/accounts after approval and keep stale audit columns.
///
/// NOTE: These tests are expected to go RED until the runtime enforces immutability
/// for UpdateDraftHeader/ReplaceDraftLines when ApprovalState != Draft.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GeneralJournalEntry_ImmutabilityAfterSubmit_P0Tests(PostgresTestFixture fixture)
{
    private PostgresTestFixture Fixture { get; } = fixture;

    [Fact]
    public async Task UpdateDraftHeader_WhenSubmitted_Throws_AndDoesNotMutate()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId) = await EnsureMinimalAccountsAsync(host);

        var docDateUtc = new DateTime(2026, 01, 10, 12, 0, 0, DateTimeKind.Utc);

        Guid id;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            id = await CreateDraftPreparedAsync(gje, docDateUtc, cashId, revenueId);
            await gje.SubmitAsync(id, submittedBy: "u1", ct: CancellationToken.None);
        }

        var before = await LoadHeaderSnapshotAsync(id);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();

            await FluentActions.Awaiting(() => gje.UpdateDraftHeaderAsync(
                    id,
                    new GeneralJournalEntryDraftHeaderUpdate(
                        JournalType: null,
                        ReasonCode: "NEW_REASON",
                        Memo: "NEW MEMO",
                        ExternalReference: "EXT-CHANGED",
                        AutoReverse: false,
                        AutoReverseOnUtc: null),
                    updatedBy: "u2",
                    ct: CancellationToken.None))
                .Should().ThrowAsync<DocumentWorkflowStateMismatchException>()
                .WithMessage("*Draft*");
        }

        var after = await LoadHeaderSnapshotAsync(id);
        after.Should().Be(before);
    }

    [Fact]
    public async Task ReplaceDraftLines_WhenSubmitted_Throws_AndDoesNotMutate()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId) = await EnsureMinimalAccountsAsync(host);

        var docDateUtc = new DateTime(2026, 01, 10, 12, 0, 0, DateTimeKind.Utc);

        Guid id;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            id = await CreateDraftPreparedAsync(gje, docDateUtc, cashId, revenueId);
            await gje.SubmitAsync(id, submittedBy: "u1", ct: CancellationToken.None);
        }

        var beforeLines = await LoadLinesSnapshotAsync(id);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();

            await FluentActions.Awaiting(() => gje.ReplaceDraftLinesAsync(
                    id,
                    new[]
                    {
                        new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Debit, cashId, 999m, "HACK"),
                        new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Credit, revenueId, 999m, "HACK"),
                    },
                    updatedBy: "u2",
                    ct: CancellationToken.None))
                .Should().ThrowAsync<DocumentWorkflowStateMismatchException>()
                .WithMessage("*Draft*");
        }

        var afterLines = await LoadLinesSnapshotAsync(id);
        afterLines.Should().Equal(beforeLines);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    public async Task UpdateDraftHeader_And_ReplaceDraftLines_WhenNotDraftApprovalState_Throws(string mode)
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId) = await EnsureMinimalAccountsAsync(host);

        var docDateUtc = new DateTime(2026, 01, 10, 12, 0, 0, DateTimeKind.Utc);

        Guid id;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            id = await CreateDraftPreparedAsync(gje, docDateUtc, cashId, revenueId);
            await gje.SubmitAsync(id, submittedBy: "u1", ct: CancellationToken.None);

            if (mode == "approve")
                await gje.ApproveAsync(id, approvedBy: "u2", ct: CancellationToken.None);
            else
                await gje.RejectAsync(id, rejectedBy: "u2", rejectReason: "Needs changes", ct: CancellationToken.None);
        }

        var headerBefore = await LoadHeaderSnapshotAsync(id);
        var linesBefore = await LoadLinesSnapshotAsync(id);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();

            await FluentActions.Awaiting(() => gje.UpdateDraftHeaderAsync(
                    id,
                    new GeneralJournalEntryDraftHeaderUpdate(null, "SHOULD_FAIL", "SHOULD_FAIL", null, false, null),
                    updatedBy: "u3",
                    ct: CancellationToken.None))
                .Should().ThrowAsync<DocumentWorkflowStateMismatchException>()
                .WithMessage("*Draft*");

            await FluentActions.Awaiting(() => gje.ReplaceDraftLinesAsync(
                    id,
                    new[]
                    {
                        new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Debit, cashId, 1m, null),
                        new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Credit, revenueId, 1m, null),
                    },
                    updatedBy: "u3",
                    ct: CancellationToken.None))
                .Should().ThrowAsync<DocumentWorkflowStateMismatchException>()
                .WithMessage("*Draft*");
        }

        (await LoadHeaderSnapshotAsync(id)).Should().Be(headerBefore);
        (await LoadLinesSnapshotAsync(id)).Should().Equal(linesBefore);

        // And still not posted
        (await GetRegisterCountAsync(id)).Should().Be(0);
    }

    private sealed record HeaderSnapshot(short ApprovalState, string? ReasonCode, string? Memo, string? ExternalReference, bool AutoReverse, DateOnly? AutoReverseOnUtc);

    private sealed record LineSnapshot(int LineNo, short Side, Guid AccountId, decimal Amount, string? Memo, Guid DimensionSetId);

    private static async Task<Guid> CreateDraftPreparedAsync(
        IGeneralJournalEntryDocumentService gje,
        DateTime docDateUtc,
        Guid cashId,
        Guid revenueId)
    {
        var id = await gje.CreateDraftAsync(docDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

        await gje.UpdateDraftHeaderAsync(
            id,
            new GeneralJournalEntryDraftHeaderUpdate(
                JournalType: null,
                ReasonCode: "TEST",
                Memo: "Initial",
                ExternalReference: "EXT-1",
                AutoReverse: false,
                AutoReverseOnUtc: null),
            updatedBy: "u1",
            ct: CancellationToken.None);

        await gje.ReplaceDraftLinesAsync(
            id,
            new[]
            {
                new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Debit, cashId, 10m, "D"),
                new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Credit, revenueId, 10m, "C"),
            },
            updatedBy: "u1",
            ct: CancellationToken.None);

        return id;
    }

    private async Task<HeaderSnapshot> LoadHeaderSnapshotAsync(Guid documentId)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT approval_state, reason_code, memo, external_reference, auto_reverse, auto_reverse_on_utc FROM doc_general_journal_entry WHERE document_id = @d",
            conn);
        cmd.Parameters.AddWithValue("d", documentId);

        await using var r = await cmd.ExecuteReaderAsync();
        (await r.ReadAsync()).Should().BeTrue();

        return new HeaderSnapshot(
            ApprovalState: r.GetInt16(0),
            ReasonCode: r.IsDBNull(1) ? null : r.GetString(1),
            Memo: r.IsDBNull(2) ? null : r.GetString(2),
            ExternalReference: r.IsDBNull(3) ? null : r.GetString(3),
            AutoReverse: r.GetBoolean(4),
            AutoReverseOnUtc: r.IsDBNull(5) ? null : DateOnly.FromDateTime(r.GetDateTime(5))
        );
    }

    private async Task<IReadOnlyList<LineSnapshot>> LoadLinesSnapshotAsync(Guid documentId)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT line_no, side, account_id, amount, memo, dimension_set_id FROM doc_general_journal_entry__lines WHERE document_id = @d ORDER BY line_no",
            conn);
        cmd.Parameters.AddWithValue("d", documentId);

        var rows = new List<LineSnapshot>();
        await using var r = await cmd.ExecuteReaderAsync();

        while (await r.ReadAsync())
        {
            rows.Add(new LineSnapshot(
                LineNo: r.GetInt32(0),
                Side: r.GetInt16(1),
                AccountId: r.GetGuid(2),
                Amount: r.GetDecimal(3),
                Memo: r.IsDBNull(4) ? null : r.GetString(4),
                DimensionSetId: r.GetGuid(5)
            ));
        }

        rows.Should().NotBeEmpty();
        return rows;
    }

    private async Task<int> GetRegisterCountAsync(Guid documentId)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var count = (int)(await new NpgsqlCommand(
            "SELECT COUNT(*)::int FROM accounting_register_main WHERE document_id = @d",
            conn)
        {
            Parameters = { new("d", documentId) }
        }.ExecuteScalarAsync())!;

        return count;
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
