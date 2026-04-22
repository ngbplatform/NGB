using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Core.Documents;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// P0: Defense-in-depth for the common document header (table: documents).
/// A posted document must not be updated/deleted via direct SQL bypass.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostedDocumentHeaderImmutabilityGuard_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string Initiator = "tests";

    [Fact]
    public async Task PostedDocumentHeader_UpdateOrDelete_IsForbidden_ByDbTrigger()
    {
        using var host = CreateHost();

        var (cashId, equityId) = await CreateMinimalPostingAccountsAsync(host);
        var dateUtc = new DateTime(2026, 01, 20, 12, 00, 00, DateTimeKind.Utc);
        var documentId = await CreateDraftGjeWithBalancedLinesAsync(host, dateUtc, cashId, equityId);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            await svc.SubmitAsync(documentId, submittedBy: "submitter", CancellationToken.None);
            await svc.ApproveAsync(documentId, approvedBy: "approver", CancellationToken.None);
            // In this platform snapshot, approval is a separate step from posting.
            // Posting moves the common document header (documents.status) to Posted.
            await svc.PostApprovedAsync(documentId, postedBy: "poster", CancellationToken.None);
        }

        // Sanity: the document is posted.
        (await GetDocumentStatusAsync(documentId)).Should().Be(DocumentStatus.Posted);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        // Direct SQL update must be rejected.
        var update = async () =>
            await conn.ExecuteAsync(
                "UPDATE documents SET number = 'HACK', updated_at_utc = updated_at_utc WHERE id = @id;",
                new { id = documentId });

        var updateEx = await update.Should().ThrowAsync<PostgresException>();
        updateEx.Which.SqlState.Should().Be("55000");
        updateEx.Which.MessageText.Should().Contain("posted");

        // Direct SQL delete must be rejected.
        var del = async () =>
            await conn.ExecuteAsync(
                "DELETE FROM documents WHERE id = @id;",
                new { id = documentId });

        var delEx = await del.Should().ThrowAsync<PostgresException>();
        delEx.Which.SqlState.Should().Be("55000");
        delEx.Which.MessageText.Should().Contain("posted");
    }

    private IHost CreateHost() => IntegrationHostFactory.Create(Fixture.ConnectionString);

    private static async Task<(Guid cashId, Guid equityId)> CreateMinimalPostingAccountsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        var cashId = await coa.CreateAsync(new CreateAccountRequest(
                Code: "50",
                Name: "Cash",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true),
            CancellationToken.None);

        var equityId = await coa.CreateAsync(new CreateAccountRequest(
                Code: "90.1",
                Name: "Retained Earnings",
                Type: AccountType.Equity,
                StatementSection: StatementSection.Equity,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true),
            CancellationToken.None);

        return (cashId, equityId);
    }

    private static async Task<Guid> CreateDraftGjeWithBalancedLinesAsync(
        IHost host,
        DateTime dateUtc,
        Guid debitAccountId,
        Guid creditAccountId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();

        var documentId = await svc.CreateDraftAsync(dateUtc, initiatedBy: Initiator, CancellationToken.None);

        await svc.UpdateDraftHeaderAsync(
            documentId,
            new GeneralJournalEntryDraftHeaderUpdate(
                JournalType: GeneralJournalEntryModels.JournalType.Adjusting,
                ReasonCode: "ADJ",
                Memo: "Integration test",
                ExternalReference: null,
                AutoReverse: false,
                AutoReverseOnUtc: null),
            updatedBy: Initiator,
            CancellationToken.None);

        var lines = new List<GeneralJournalEntryDraftLineInput>
        {
            new(
                Side: GeneralJournalEntryModels.LineSide.Debit,
                AccountId: debitAccountId,
                Amount: 100m,
                Memo: "D"),
            new(
                Side: GeneralJournalEntryModels.LineSide.Credit,
                AccountId: creditAccountId,
                Amount: 100m,
                Memo: "C"),
        };

        await svc.ReplaceDraftLinesAsync(documentId, lines, updatedBy: Initiator, CancellationToken.None);
        return documentId;
    }

    private async Task<DocumentStatus> GetDocumentStatusAsync(Guid documentId)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        var status = await conn.ExecuteScalarAsync<short>(
            "SELECT status FROM documents WHERE id = @id;",
            new { id = documentId });

        return (DocumentStatus)status;
    }
}
