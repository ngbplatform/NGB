using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Core.Documents;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Persistence.Documents;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.GeneralJournalEntry;

[Collection(PostgresCollection.Name)]
public sealed class GeneralJournalEntry_Allocations_Semantics_P2Tests(PostgresTestFixture fixture)
{
    private PostgresTestFixture Fixture { get; } = fixture;

    [Fact]
    public async Task PostApproved_WaterfallAllocations_2x2_WritesExpectedAllocationMatrix_AndRegisterRowsMatch()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId) = await EnsureMinimalAccountsAsync(host);

        // Line order is significant: allocations are built by consuming credits in order.
        // Lines: D70, D30, C50, C50
        // Expected allocations:
        //  1) D1 -> C3 : 50
        //  2) D1 -> C4 : 20
        //  3) D2 -> C4 : 30
        var docId = await CreateAndPostManualAsync(
            host,
            docDateUtc: new DateTime(2026, 01, 10, 12, 0, 0, DateTimeKind.Utc),
            cashId,
            revenueId,
            lines: new[]
            {
                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Debit,
                    AccountId: cashId,
                    Amount: 70m,
                    Memo: "D1"),
                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Debit,
                    AccountId: cashId,
                    Amount: 30m,
                    Memo: "D2"),
                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Credit,
                    AccountId: revenueId,
                    Amount: 50m,
                    Memo: "C1"),
                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Credit,
                    AccountId: revenueId,
                    Amount: 50m,
                    Memo: "C2"),
            });

        var allocations = await ReadAllocationsAsync(docId);

        allocations.Should().Equal(
            new AllocationRow(EntryNo: 1, DebitLineNo: 1, CreditLineNo: 3, Amount: 50m),
            new AllocationRow(EntryNo: 2, DebitLineNo: 1, CreditLineNo: 4, Amount: 20m),
            new AllocationRow(EntryNo: 3, DebitLineNo: 2, CreditLineNo: 4, Amount: 30m));

        await AssertRegisterRowsMatchAllocationsAsync(docId, expectedAllocationsCount: allocations.Count, expectedTotalAmount: 100m);
    }

    [Fact]
    public async Task PostApproved_WaterfallAllocations_3x2_WritesExpectedAllocationMatrix_AndSatisfiesSumInvariants()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId) = await EnsureMinimalAccountsAsync(host);

        // Lines: D10, D20, D30, C25, C35
        // Expected allocations:
        //  1) D1 -> C4 : 10 (C4 rem 15)
        //  2) D2 -> C4 : 15 (C4 rem 0, D2 rem 5)
        //  3) D2 -> C5 : 5  (C5 rem 30)
        //  4) D3 -> C5 : 30 (C5 rem 0)
        var docId = await CreateAndPostManualAsync(
            host,
            docDateUtc: new DateTime(2026, 02, 10, 12, 0, 0, DateTimeKind.Utc),
            cashId,
            revenueId,
            lines:
            [
                new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Debit, cashId, 10m, "D1"),
                new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Debit, cashId, 20m, "D2"),
                new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Debit, cashId, 30m, "D3"),
                new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Credit, revenueId, 25m, "C1"),
                new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Credit, revenueId, 35m, "C2")
            ]);

        var allocations = await ReadAllocationsAsync(docId);

        allocations.Should().Equal(
            new AllocationRow(EntryNo: 1, DebitLineNo: 1, CreditLineNo: 4, Amount: 10m),
            new AllocationRow(EntryNo: 2, DebitLineNo: 2, CreditLineNo: 4, Amount: 15m),
            new AllocationRow(EntryNo: 3, DebitLineNo: 2, CreditLineNo: 5, Amount: 5m),
            new AllocationRow(EntryNo: 4, DebitLineNo: 3, CreditLineNo: 5, Amount: 30m));

        // Sum invariants: by debit line and by credit line.
        allocations.Where(a => a.DebitLineNo == 1).Sum(a => a.Amount).Should().Be(10m);
        allocations.Where(a => a.DebitLineNo == 2).Sum(a => a.Amount).Should().Be(20m);
        allocations.Where(a => a.DebitLineNo == 3).Sum(a => a.Amount).Should().Be(30m);

        allocations.Where(a => a.CreditLineNo == 4).Sum(a => a.Amount).Should().Be(25m);
        allocations.Where(a => a.CreditLineNo == 5).Sum(a => a.Amount).Should().Be(35m);

        allocations.Sum(a => a.Amount).Should().Be(60m);

        await AssertRegisterRowsMatchAllocationsAsync(docId, expectedAllocationsCount: allocations.Count, expectedTotalAmount: 60m);
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

    private static async Task<Guid> CreateAndPostManualAsync(
        IHost host,
        DateTime docDateUtc,
        Guid cashId,
        Guid revenueId,
        IReadOnlyList<GeneralJournalEntryDraftLineInput> lines)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();

        var docId = await gje.CreateDraftAsync(docDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

        await gje.UpdateDraftHeaderAsync(
            docId,
            new GeneralJournalEntryDraftHeaderUpdate(
                JournalType: null,
                ReasonCode: "ALLOC_TEST",
                Memo: "Allocation semantics",
                ExternalReference: null,
                AutoReverse: false,
                AutoReverseOnUtc: null),
            updatedBy: "u1",
            ct: CancellationToken.None);

        // Use provided line order as-is; NormalizeLines assigns sequential line_no.
        await gje.ReplaceDraftLinesAsync(docId, lines, updatedBy: "u1", ct: CancellationToken.None);

        await gje.SubmitAsync(docId, submittedBy: "u1", ct: CancellationToken.None);
        await gje.ApproveAsync(docId, approvedBy: "u2", ct: CancellationToken.None);
        await gje.PostApprovedAsync(docId, postedBy: "u2", ct: CancellationToken.None);

        // Sanity: common document must become Posted
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        (await docs.GetAsync(docId, CancellationToken.None))!.Status.Should().Be(DocumentStatus.Posted);

        return docId;
    }

    private async Task<List<AllocationRow>> ReadAllocationsAsync(Guid documentId)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT entry_no, debit_line_no, credit_line_no, amount::numeric FROM doc_general_journal_entry__allocations WHERE document_id = @d ORDER BY entry_no",
            conn);
        cmd.Parameters.AddWithValue("d", documentId);

        var result = new List<AllocationRow>();
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            result.Add(new AllocationRow(
                EntryNo: reader.GetInt32(0),
                DebitLineNo: reader.GetInt32(1),
                CreditLineNo: reader.GetInt32(2),
                Amount: reader.GetDecimal(3)));
        }

        return result;
    }

    private async Task AssertRegisterRowsMatchAllocationsAsync(Guid documentId, int expectedAllocationsCount, decimal expectedTotalAmount)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var (count, sum) = await ReadRegisterStatsAsync(conn, documentId);
        count.Should().Be(expectedAllocationsCount, "each allocation produces exactly one accounting movement row");
        sum.Should().Be(expectedTotalAmount);
    }

    private static async Task<(int count, decimal sumAmount)> ReadRegisterStatsAsync(NpgsqlConnection conn, Guid documentId)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*)::int, COALESCE(SUM(amount),0)::numeric FROM accounting_register_main WHERE document_id = @d",
            conn);
        cmd.Parameters.AddWithValue("d", documentId);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        return (reader.GetInt32(0), reader.GetDecimal(1));
    }

    private sealed record AllocationRow(int EntryNo, int DebitLineNo, int CreditLineNo, decimal Amount);
}
