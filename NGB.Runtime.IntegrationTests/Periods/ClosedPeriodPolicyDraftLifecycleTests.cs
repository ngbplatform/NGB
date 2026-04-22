using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Balances;
using NGB.Accounting.PostingState.Readers;
using NGB.Accounting.Registers;
using NGB.Accounting.Turnovers;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Persistence.Documents;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class ClosedPeriodPolicyDraftLifecycleTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CreateDraftAsync_DocumentInClosedPeriod_Succeeds_AndDoesNotTouchAccountingState()
    {
        var logWindow = PostingLogTestWindow.Capture();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>());

        // IMPORTANT: many runtime services are registered as Scoped. IntegrationHostFactory returns a root provider,
        // so we must resolve scoped services from an explicit scope.
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        // Close the month first (the whole point of this policy test).
        // We still must be able to create drafts inside a closed accounting period.
        var period = new DateOnly(2026, 1, 1);
        var closing = sp.GetRequiredService<IPeriodClosingService>();
        await closing.CloseMonthAsync(period, closedBy: "test", ct: CancellationToken.None);

        var snapshotBefore = await DraftPolicyTestHelpers.SnapshotAsync(sp, documentId: null, period, logWindow);

        var drafts = sp.GetRequiredService<IDocumentDraftService>();

        var draftDateUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var draftId = await drafts.CreateDraftAsync(
            typeCode: "demo.sales_invoice",
            number: "DRAFT-1",
            dateUtc: draftDateUtc,
            manageTransaction: true,
            ct: CancellationToken.None);

        draftId.Should().NotBeEmpty();

        // Ensure the draft row exists and is Draft.
        var docRepo = sp.GetRequiredService<IDocumentRepository>();
        var doc = await docRepo.GetAsync(draftId, CancellationToken.None);

        doc.Should().NotBeNull();
        doc!.Id.Should().Be(draftId);
        doc.TypeCode.Should().Be("demo.sales_invoice");
        doc.Number.Should().Be("DRAFT-1");
        doc.DateUtc.Should().Be(draftDateUtc);
        doc.Status.Should().Be(DocumentStatus.Draft);

        var snapshotAfter = await DraftPolicyTestHelpers.SnapshotAsync(sp, draftId, period, logWindow);

        // Creating a draft must NOT touch accounting state and must NOT create posting_log entries.
        snapshotAfter.DocumentEntries.Should().BeEmpty("drafts must not create accounting register entries");
        snapshotAfter.Turnovers.Should().BeEquivalentTo(snapshotBefore.Turnovers);
        snapshotAfter.Balances.Should().BeEquivalentTo(snapshotBefore.Balances);

        // No posting log created for this draft (no InProgress, no Completed, no \"zombie\" rows).
        snapshotAfter.PostingLogRowsForDocument.Should().BeEmpty();
        snapshotAfter.PostingLogInProgressForDocument.Should().BeEmpty();
        snapshotAfter.PostingLogCompletedForDocument.Should().BeEmpty();

        // Draft lifecycle must not touch posting_log at all (no unrelated side effects).
        snapshotAfter.PostingLogAll.Should().BeEquivalentTo(snapshotBefore.PostingLogAll);
    }
}

internal static class DraftPolicyTestHelpers
{
    public static async Task<DraftPolicySnapshot> SnapshotAsync(
        IServiceProvider sp,
        Guid? documentId,
        DateOnly period,
        PostingLogTestWindow logWindow)
    {
        var ct = CancellationToken.None;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var documentEntries = documentId is null
            ? []
            : (await entryReader.GetByDocumentAsync(documentId.Value, ct)).ToList();

        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        var turnovers = (await turnoverReader.GetForPeriodAsync(period, ct)).ToList();

        var balanceReader = sp.GetRequiredService<IAccountingBalanceReader>();
        var balances = (await balanceReader.GetForPeriodAsync(period, ct)).ToList();

        var logReader = sp.GetRequiredService<IPostingStateReader>();

        async Task<IReadOnlyList<PostingStateRecord>> ReadLogsAsync(Guid? docId, PostingStateStatus? status = null)
        {
            var page = await logReader.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = logWindow.FromUtc,
                ToUtc = logWindow.ToUtc,
                DocumentId = docId,
                Status = status,
                PageSize = 10_000,
            }, ct);

            return page.Records.ToList();
        }

        var logsForDocument = await ReadLogsAsync(documentId);
        var logsInProgressForDocument = await ReadLogsAsync(documentId, PostingStateStatus.InProgress);
        var logsCompletedForDocument = await ReadLogsAsync(documentId, PostingStateStatus.Completed);
        var logsAll = await ReadLogsAsync(docId: null);

        return new DraftPolicySnapshot(
            DocumentEntries: documentEntries,
            Turnovers: turnovers,
            Balances: balances,
            PostingLogAll: logsAll,
            PostingLogRowsForDocument: logsForDocument,
            PostingLogInProgressForDocument: logsInProgressForDocument,
            PostingLogCompletedForDocument: logsCompletedForDocument);
    }
}

internal sealed record DraftPolicySnapshot(
    IReadOnlyList<AccountingEntry> DocumentEntries,
    IReadOnlyList<AccountingTurnover> Turnovers,
    IReadOnlyList<AccountingBalance> Balances,
    IReadOnlyList<PostingStateRecord> PostingLogAll,
    IReadOnlyList<PostingStateRecord> PostingLogRowsForDocument,
    IReadOnlyList<PostingStateRecord> PostingLogInProgressForDocument,
    IReadOnlyList<PostingStateRecord> PostingLogCompletedForDocument);
