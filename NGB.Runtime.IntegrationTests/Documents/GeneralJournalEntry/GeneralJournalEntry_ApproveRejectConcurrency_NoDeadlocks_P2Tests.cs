using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.GeneralJournalEntry;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.Documents.Workflow;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.GeneralJournalEntry;

[Collection(PostgresCollection.Name)]
public sealed class GeneralJournalEntry_ApproveRejectConcurrency_NoDeadlocks_P2Tests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task ApproveAndReject_SubmittedConcurrently_ExactlyOneWins_AndStateIsConsistent()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var docDateUtc = new DateTime(2026, 03, 05, 12, 0, 0, DateTimeKind.Utc);
        var documentId = await CreateSubmittedDraftAsync(host, docDateUtc, cashId, revenueId);

        using var barrier = new Barrier(participantCount: 2);

        var approve = Task.Run(() => TryAsync(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();

            barrier.SignalAndWaitOrThrow(TimeSpan.FromSeconds(10));
            await gje.ApproveAsync(documentId, approvedBy: "u2", ct: CancellationToken.None);
        }));

        var reject = Task.Run(() => TryAsync(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();

            barrier.SignalAndWaitOrThrow(TimeSpan.FromSeconds(10));
            await gje.RejectAsync(documentId, rejectedBy: "u3", rejectReason: "Not acceptable", ct: CancellationToken.None);
        }));

        var results = await Task.WhenAll(approve, reject)
            .WaitAsync(TimeSpan.FromSeconds(30));

        results.Count(x => x is null).Should().Be(1, "exactly one of Approve/Reject should succeed under document lock");
        results.Count(x => x is not null).Should().Be(1);

        var failure = results.Single(x => x is not null)!;
        var mismatch = failure.Should().BeOfType<DocumentWorkflowStateMismatchException>().Subject;
        mismatch.ErrorCode.Should().Be(DocumentWorkflowStateMismatchException.ErrorCodeConst);
        failure.Message.Should().Match("Expected Submitted state, got *");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var gjeRepo = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryRepository>();

            var doc = await docs.GetAsync(documentId, CancellationToken.None);
            doc.Should().NotBeNull();
            doc!.Status.Should().Be(DocumentStatus.Draft, "approval workflow does not post the document");
            doc.Number.Should().NotBeNullOrWhiteSpace("number is assigned on submit/approve");

            var header = await gjeRepo.GetHeaderAsync(documentId, CancellationToken.None);
            header.Should().NotBeNull();
            header!.SubmittedBy.Should().Be("u1");
            header.SubmittedAtUtc.Should().NotBeNull();

            if (header.ApprovalState == GeneralJournalEntryModels.ApprovalState.Approved)
            {
                header.ApprovedBy.Should().Be("u2");
                header.ApprovedAtUtc.Should().NotBeNull();

                header.RejectedBy.Should().BeNull();
                header.RejectedAtUtc.Should().BeNull();
                header.RejectReason.Should().BeNull();
            }
            else
            {
                header.ApprovalState.Should().Be(GeneralJournalEntryModels.ApprovalState.Rejected);
                header.RejectedBy.Should().Be("u3");
                header.RejectedAtUtc.Should().NotBeNull();
                header.RejectReason.Should().Be("Not acceptable");

                header.ApprovedBy.Should().BeNull();
                header.ApprovedAtUtc.Should().BeNull();
            }
        }

        await using (var conn = new NpgsqlConnection(fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var regCount = (int)(await new NpgsqlCommand(
                "SELECT COUNT(*)::int FROM accounting_register_main WHERE document_id = @d",
                conn)
            {
                Parameters = { new("d", documentId) }
            }.ExecuteScalarAsync(CancellationToken.None))!;

            regCount.Should().Be(0, "Approve/Reject must not write accounting movements");
        }
    }

    private static async Task<Guid> CreateSubmittedDraftAsync(
        IHost host,
        DateTime docDateUtc,
        Guid cashId,
        Guid revenueId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();

        var id = await gje.CreateDraftAsync(docDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

        await gje.UpdateDraftHeaderAsync(
            id,
            new GeneralJournalEntryDraftHeaderUpdate(
                JournalType: null,
                ReasonCode: "APPROVAL_RACE",
                Memo: "Approve/Reject concurrency",
                ExternalReference: null,
                AutoReverse: false,
                AutoReverseOnUtc: null),
            updatedBy: "u1",
            ct: CancellationToken.None);

        await gje.ReplaceDraftLinesAsync(
            id,
            new[]
            {
                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Debit,
                    AccountId: cashId,
                    Amount: 100m,
                    Memo: null),
                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Credit,
                    AccountId: revenueId,
                    Amount: 100m,
                    Memo: null),
            },
            updatedBy: "u1",
            ct: CancellationToken.None);

        await gje.SubmitAsync(id, submittedBy: "u1", ct: CancellationToken.None);

        return id;
    }

    private static async Task<Exception?> TryAsync(Func<Task> op)
    {
        try
        {
            await op();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
