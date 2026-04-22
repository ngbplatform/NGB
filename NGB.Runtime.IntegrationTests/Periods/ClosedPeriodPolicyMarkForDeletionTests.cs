using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Balances;
using NGB.Accounting.PostingState.Readers;
using NGB.Accounting.Registers;
using NGB.Accounting.Turnovers;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Persistence.Documents;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class ClosedPeriodPolicyMarkForDeletionTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task MarkForDeletionAsync_DraftDocumentInClosedPeriod_Succeeds_AndDoesNotTouchAccountingState()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();

        var logWindow = PostingLogTestWindow.Capture();

        using var host = IntegrationHostFactory.Create(
            fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>());
        
        var docDateUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var period = new DateOnly(docDateUtc.Year, docDateUtc.Month, 1);

        await SeedMinimalCoaAsync(host);

        // Create Draft document dated inside the month that will be closed.
        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            documentId = await drafts.CreateDraftAsync(
                typeCode: "test_doc",
                number: "T-001",
                dateUtc: docDateUtc,
                manageTransaction: true,
                ct: CancellationToken.None);
        }

        // Close the month.
        await CloseMonthAsync(host, period);

        // Snapshot state BEFORE MarkForDeletion.
        var before = await SnapshotAsync(host, documentId, logWindow);

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.MarkForDeletionAsync(documentId, CancellationToken.None);
        }

        // Assert
        var after = await SnapshotAsync(host, documentId, logWindow);

        // Document state changed as expected.
        after.Document.Should().NotBeNull();
        after.Document!.Status.Should().Be(DocumentStatus.MarkedForDeletion);
        after.Document.MarkedForDeletionAtUtc.Should().NotBeNull();

        // Accounting state must not change (no posting/unposting/reposting happens here).
        after.Entries.Should().BeEquivalentTo(before.Entries);
        after.Turnovers.Should().BeEquivalentTo(before.Turnovers);
        after.Balances.Should().BeEquivalentTo(before.Balances);

        // MarkForDeletion is NOT an accounting posting operation, so posting_log remains unchanged.
        after.PostingLogForDocument.Should().BeEquivalentTo(before.PostingLogForDocument);
        after.PostingLogAll.Should().BeEquivalentTo(before.PostingLogAll);
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }

    private static async Task CloseMonthAsync(IHost host, DateOnly period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
        await closing.CloseMonthAsync(period, closedBy: "test", CancellationToken.None);
    }

    private static async Task<StateSnapshot> SnapshotAsync(IHost host, Guid documentId, PostingLogTestWindow logWindow)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var documents = sp.GetRequiredService<IDocumentRepository>();
        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        var balanceReader = sp.GetRequiredService<IAccountingBalanceReader>();
        var postingLogReader = sp.GetRequiredService<IPostingStateReader>();

        var doc = await documents.GetAsync(documentId, CancellationToken.None);

        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);

        var docPeriod = doc is null
            ? default
            : new DateOnly(doc.DateUtc.Year, doc.DateUtc.Month, 1);

        var turnovers = doc is null
            ? []
            : await turnoverReader.GetForPeriodAsync(docPeriod, CancellationToken.None);

        var balances = doc is null
            ? []
            : await balanceReader.GetForPeriodAsync(docPeriod, CancellationToken.None);

        // posting_log uses operation timestamps (UTC now), so read with a wide window.
        var pageForDocument = await postingLogReader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = logWindow.FromUtc,
            ToUtc = logWindow.ToUtc,
            DocumentId = documentId,
            PageSize = 10_000,
        }, CancellationToken.None);

        var pageAll = await postingLogReader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = logWindow.FromUtc,
            ToUtc = logWindow.ToUtc,
            DocumentId = null,
            Operation = null,
            Status = null,
            PageSize = 10_000,
        }, CancellationToken.None);

        return new StateSnapshot(
            Document: doc,
            Entries: entries.ToList(),
            Turnovers: turnovers.ToList(),
            Balances: balances.ToList(),
            PostingLogAll: pageAll.Records.ToList(),
            PostingLogForDocument: pageForDocument.Records.ToList());
    }

    private sealed record StateSnapshot(
        DocumentRecord? Document,
        List<AccountingEntry> Entries,
        List<AccountingTurnover> Turnovers,
        List<AccountingBalance> Balances,
        List<PostingStateRecord> PostingLogAll,
        List<PostingStateRecord> PostingLogForDocument);
}
