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

/// <summary>
/// P0: ReversePostedAsync must be concurrency-safe and idempotent.
/// Two concurrent callers must end up with exactly one reversal document and exactly one set of movements.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GeneralJournalEntry_ReversePosted_Concurrency_P0Tests(PostgresTestFixture fixture)
{
    private PostgresTestFixture Fixture { get; } = fixture;

    [Fact]
    public async Task ReversePosted_TwoConcurrentCalls_ReturnSameId_AndOnlyOneReversalExists()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId) = await EnsureMinimalAccountsAsync(host);

        var originalDateUtc = new DateTime(2026, 01, 10, 12, 0, 0, DateTimeKind.Utc);
        var reversalDateUtc = new DateTime(2026, 01, 11, 0, 0, 0, DateTimeKind.Utc);

        Guid originalId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            originalId = await gje.CreateDraftAsync(originalDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

            await gje.UpdateDraftHeaderAsync(
                originalId,
                new GeneralJournalEntryDraftHeaderUpdate(
                    JournalType: null,
                    ReasonCode: "CORRECTION",
                    Memo: "Will be reversed",
                    ExternalReference: null,
                    AutoReverse: false,
                    AutoReverseOnUtc: null),
                updatedBy: "u1",
                ct: CancellationToken.None);

            await gje.ReplaceDraftLinesAsync(
                originalId,
                [
                    new GeneralJournalEntryDraftLineInput(
                        Side: GeneralJournalEntryModels.LineSide.Debit,
                        AccountId: cashId,
                        Amount: 10m,
                        Memo: null),
                    new GeneralJournalEntryDraftLineInput(
                        Side: GeneralJournalEntryModels.LineSide.Credit,
                        AccountId: revenueId,
                        Amount: 10m,
                        Memo: null)
                ],
                updatedBy: "u1",
                ct: CancellationToken.None);

            await gje.SubmitAsync(originalId, submittedBy: "u1", ct: CancellationToken.None);
            await gje.ApproveAsync(originalId, approvedBy: "u2", ct: CancellationToken.None);
            await gje.PostApprovedAsync(originalId, postedBy: "u2", ct: CancellationToken.None);
        }

        // Act: two concurrent reversals for the same original and the same reversal date.
        using var barrier = new Barrier(participantCount: 2);

        Task<Guid> t1 = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            await using var scope = host.Services.CreateAsyncScope();
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            return await gje.ReversePostedAsync(originalId, reversalDateUtc, initiatedBy: "u3", postImmediately: true, ct: CancellationToken.None);
        });

        Task<Guid> t2 = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            await using var scope = host.Services.CreateAsyncScope();
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            return await gje.ReversePostedAsync(originalId, reversalDateUtc, initiatedBy: "u3", postImmediately: true, ct: CancellationToken.None);
        });

        var results = await Task.WhenAll(t1, t2);
        results[0].Should().Be(results[1], "ReversePostedAsync must be idempotent under concurrency");

        var reversalId = results[0];

        // Assert: exactly one reversal exists and it is posted.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var rev = await docs.GetAsync(reversalId, CancellationToken.None);
            rev.Should().NotBeNull();
            rev!.Status.Should().Be(DocumentStatus.Posted);
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var reversalCount = (int)(await new NpgsqlCommand(
                "SELECT COUNT(*)::int FROM doc_general_journal_entry WHERE reversal_of_document_id = @o",
                conn)
            {
                Parameters = { new("o", originalId) }
            }.ExecuteScalarAsync())!;

            reversalCount.Should().Be(1, "unique reversal constraint + advisory locks must prevent duplicates");

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

        var cashId = await mgmt.CreateAsync(
            new CreateAccountRequest(
                Code: "1000",
                Name: "Cash",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow),
            CancellationToken.None);

        var revenueId = await mgmt.CreateAsync(
            new CreateAccountRequest(
                Code: "4000",
                Name: "Revenue",
                Type: AccountType.Income,
                StatementSection: StatementSection.Income,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow),
            CancellationToken.None);

        return (cashId, revenueId);
    }
}
