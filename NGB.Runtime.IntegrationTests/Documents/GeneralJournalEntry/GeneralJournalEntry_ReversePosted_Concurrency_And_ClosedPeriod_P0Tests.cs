using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.PostingState;
using NGB.Core.Documents;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.GeneralJournalEntry;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;
using NGB.Runtime.Posting;

namespace NGB.Runtime.IntegrationTests.Documents.GeneralJournalEntry;

[Collection(PostgresCollection.Name)]
public sealed class GeneralJournalEntry_ReversePosted_Concurrency_And_ClosedPeriod_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ReversePosted_PostImmediatelyFalse_CreatesDraftApprovedSystemReversal_WithFlippedLines_AndNoRegisterWrites()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var originalId = await CreateAndPostManualOriginalAsync(
            host,
            dateUtc: new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc),
            cashId,
            revenueId,
            amount: 10m);

        var reversalDateUtc = new DateTime(2026, 2, 5, 0, 0, 0, DateTimeKind.Utc);
        var reversalDateOnly = DateOnly.FromDateTime(reversalDateUtc);
        var expectedReversalId = DeterministicGuid.Create($"gje:reversal:{originalId:N}:{reversalDateOnly:yyyy-MM-dd}");

        Guid reversalId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            reversalId = await svc.ReversePostedAsync(
                originalId,
                reversalDateUtc,
                initiatedBy: "u3",
                postImmediately: false,
                ct: CancellationToken.None);
        }

        reversalId.Should().Be(expectedReversalId, "ReversePosted uses deterministic id to guarantee idempotency");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var repo = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryRepository>();

            var reversalDoc = await docs.GetAsync(reversalId, CancellationToken.None);
            reversalDoc.Should().NotBeNull();
            reversalDoc!.Status.Should().Be(DocumentStatus.Draft);
            reversalDoc.PostedAtUtc.Should().BeNull();

            var originalDoc = await docs.GetAsync(originalId, CancellationToken.None);
            originalDoc.Should().NotBeNull();

            var header = await repo.GetHeaderAsync(reversalId, CancellationToken.None);
            header.Should().NotBeNull();
            header!.Source.Should().Be(GeneralJournalEntryModels.Source.System);
            header.JournalType.Should().Be(GeneralJournalEntryModels.JournalType.Reversing);
            header.ApprovalState.Should().Be(GeneralJournalEntryModels.ApprovalState.Approved);
            header.ReversalOfDocumentId.Should().Be(originalId);
            header.Memo.Should().Be($"Reversal of {BuildExpectedDisplay(originalDoc!)}");

            var originalLines = await repo.GetLinesAsync(originalId, CancellationToken.None);
            originalLines.Should().HaveCount(2);

            var oDebit = originalLines.Single(l => l.Side == GeneralJournalEntryModels.LineSide.Debit);
            var oCredit = originalLines.Single(l => l.Side == GeneralJournalEntryModels.LineSide.Credit);

            var reversalLines = await repo.GetLinesAsync(reversalId, CancellationToken.None);
            reversalLines.Should().HaveCount(2);

            reversalLines.Should().ContainSingle(l =>
                l.Side == GeneralJournalEntryModels.LineSide.Debit &&
                l.AccountId == oCredit.AccountId &&
                l.Amount == oCredit.Amount);

            reversalLines.Should().ContainSingle(l =>
                l.Side == GeneralJournalEntryModels.LineSide.Credit &&
                l.AccountId == oDebit.AccountId &&
                l.Amount == oDebit.Amount);

            (await repo.GetAllocationsAsync(reversalId, CancellationToken.None))
                .Should().BeEmpty("allocations are derived on posting");
        }

        (await CountRegisterRowsAsync(Fixture.ConnectionString, reversalId)).Should().Be(0);
        (await CountPostingLogRowsAsync(Fixture.ConnectionString, reversalId, PostingOperation.Post)).Should().Be(0);
    }

    [Fact]
    public async Task SystemReversal_Posting_ConcurrentServiceVsRunner_ExactlyOnce_NoDuplicateRegisterWrites()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var originalId = await CreateAndPostManualOriginalAsync(
            host,
            dateUtc: new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc),
            cashId,
            revenueId,
            amount: 10m);

        var reversalDateUtc = new DateTime(2026, 2, 5, 0, 0, 0, DateTimeKind.Utc);
        var reversalDateOnly = DateOnly.FromDateTime(reversalDateUtc);

        Guid reversalId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            reversalId = await svc.ReversePostedAsync(
                originalId,
                reversalDateUtc,
                initiatedBy: "u3",
                postImmediately: false,
                ct: CancellationToken.None);
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runnerTask = Task.Run(async () =>
        {
            await start.Task;
            await using var scope = host.Services.CreateAsyncScope();
            var runner = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntrySystemReversalRunner>();
            return await runner.PostDueSystemReversalsAsync(
                reversalDateOnly,
                batchSize: 50,
                postedBy: "SYSTEM",
                ct: CancellationToken.None);
        });

        var manualPostTask = Task.Run(async () =>
        {
            await start.Task;
            await using var scope = host.Services.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            await svc.PostApprovedAsync(reversalId, postedBy: "u9", ct: CancellationToken.None);
        });

        start.SetResult();
        await Task.WhenAll(runnerTask, manualPostTask);

        (await CountRegisterRowsAsync(Fixture.ConnectionString, reversalId)).Should().Be(1,
            "idempotency + per-document locks must prevent double posting");

        (await CountPostingLogRowsAsync(Fixture.ConnectionString, reversalId, PostingOperation.Post)).Should().Be(1,
            "accounting_posting_state is the single source of truth for idempotent posting");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var reversalDoc = await docs.GetAsync(reversalId, CancellationToken.None);
            reversalDoc.Should().NotBeNull();
            reversalDoc!.Status.Should().Be(DocumentStatus.Posted);
            reversalDoc.PostedAtUtc.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task ReversePosted_PostImmediatelyTrue_WhenReversalPeriodClosed_Throws_ButDraftReversalPersists_AndNoRegisterWrites()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        // Close March 2026 upfront.
        var closedPeriod = new DateOnly(2026, 3, 1);
        await ReportingTestHelpers.CloseMonthAsync(host, closedPeriod, closedBy: "tests");

        var originalId = await CreateAndPostManualOriginalAsync(
            host,
            dateUtc: new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc),
            cashId,
            revenueId,
            amount: 10m);

        var reversalDateUtc = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);
        var reversalDateOnly = DateOnly.FromDateTime(reversalDateUtc);
        var expectedReversalId = DeterministicGuid.Create($"gje:reversal:{originalId:N}:{reversalDateOnly:yyyy-MM-dd}");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();

            Func<Task> act = async () =>
            {
                _ = await svc.ReversePostedAsync(
                    originalId,
                    reversalDateUtc,
                    initiatedBy: "u3",
                    postImmediately: true,
                    ct: CancellationToken.None);
            };

            await act.Should().ThrowAsync<PostingPeriodClosedException>()
                .WithMessage($"Posting is forbidden. Period is closed: {closedPeriod:yyyy-MM-dd}*");
        }

        // The reversal document is created and committed before posting is attempted.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var repo = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryRepository>();

            var original = await docs.GetAsync(originalId, CancellationToken.None);
            original.Should().NotBeNull();
            original!.Status.Should().Be(DocumentStatus.Posted);

            var reversal = await docs.GetAsync(expectedReversalId, CancellationToken.None);
            reversal.Should().NotBeNull("reversal creation is committed before post attempt");
            reversal!.Status.Should().Be(DocumentStatus.Draft);
            reversal.PostedAtUtc.Should().BeNull();

            var header = await repo.GetHeaderAsync(expectedReversalId, CancellationToken.None);
            header.Should().NotBeNull();
            header!.Source.Should().Be(GeneralJournalEntryModels.Source.System);
            header.JournalType.Should().Be(GeneralJournalEntryModels.JournalType.Reversing);
            header.ApprovalState.Should().Be(GeneralJournalEntryModels.ApprovalState.Approved);
            header.ReversalOfDocumentId.Should().Be(originalId);
            header.Memo.Should().Be($"Reversal of {BuildExpectedDisplay(original)}");
        }

        (await CountRegisterRowsAsync(Fixture.ConnectionString, expectedReversalId)).Should().Be(0,
            "posting into a closed period must have zero side effects");

        (await CountPostingLogRowsAsync(Fixture.ConnectionString, expectedReversalId, PostingOperation.Post)).Should().Be(0,
            "failed post must not mark posting_log completed");
    }

    private static async Task<Guid> CreateAndPostManualOriginalAsync(
        IHost host,
        DateTime dateUtc,
        Guid cashId,
        Guid revenueId,
        decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();

        var id = await svc.CreateDraftAsync(dateUtc, initiatedBy: "u1", ct: CancellationToken.None);

        await svc.UpdateDraftHeaderAsync(
            id,
            new GeneralJournalEntryDraftHeaderUpdate(
                JournalType: null,
                ReasonCode: "TEST",
                Memo: "Original",
                ExternalReference: null,
                AutoReverse: false,
                AutoReverseOnUtc: null),
            updatedBy: "u1",
            ct: CancellationToken.None);

        await svc.ReplaceDraftLinesAsync(
            id,
            new[]
            {
                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Debit,
                    AccountId: cashId,
                    Amount: amount,
                    Memo: null),
                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Credit,
                    AccountId: revenueId,
                    Amount: amount,
                    Memo: null),
            },
            updatedBy: "u1",
            ct: CancellationToken.None);

        await svc.SubmitAsync(id, submittedBy: "u1", ct: CancellationToken.None);
        await svc.ApproveAsync(id, approvedBy: "u2", ct: CancellationToken.None);
        await svc.PostApprovedAsync(id, postedBy: "u2", ct: CancellationToken.None);

        return id;
    }

    private static string BuildExpectedDisplay(DocumentRecord document)
    {
        var dateText = document.DateUtc.ToString("M/d/yyyy", System.Globalization.CultureInfo.InvariantCulture);
        var number = document.Number?.Trim();

        return string.IsNullOrWhiteSpace(number)
            ? $"General Journal Entry {dateText}"
            : $"General Journal Entry {number} {dateText}";
    }

    private static async Task<int> CountRegisterRowsAsync(string cs, Guid documentId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*)::int FROM accounting_register_main WHERE document_id = @d",
            conn);
        cmd.Parameters.AddWithValue("d", documentId);

        return (int)(await cmd.ExecuteScalarAsync(CancellationToken.None))!;
    }

    private static async Task<int> CountPostingLogRowsAsync(string cs, Guid documentId, PostingOperation operation)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*)::int FROM accounting_posting_state WHERE document_id = @d AND operation = @op",
            conn);
        cmd.Parameters.AddWithValue("d", documentId);
        cmd.Parameters.AddWithValue("op", (short)operation);

        return (int)(await cmd.ExecuteScalarAsync(CancellationToken.None))!;
    }
}
