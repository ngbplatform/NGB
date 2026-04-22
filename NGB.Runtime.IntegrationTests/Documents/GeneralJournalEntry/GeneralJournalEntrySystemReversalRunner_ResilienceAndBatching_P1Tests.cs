using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Persistence.Documents;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.GeneralJournalEntry;

[Collection(PostgresCollection.Name)]
public sealed class GeneralJournalEntrySystemReversalRunner_ResilienceAndBatching_P1Tests(PostgresTestFixture fixture)
{
    private PostgresTestFixture Fixture { get; } = fixture;

    [Fact]
    public async Task PostDueSystemReversalsAsync_PostsInBatches_UntilExhausted_AndIsIdempotent()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var reverseOn = new DateOnly(2026, 02, 10);
        var docDateUtc = new DateTime(2026, 02, 01, 12, 0, 0, DateTimeKind.Utc);

        var originals = new List<Guid>();
        for (var i = 0; i < 3; i++)
            originals.Add(await CreateAndPostAutoReverseOriginalAsync(host, docDateUtc.AddDays(i), cashId, revenueId, amount: 10m + i, reverseOn));

        var reversalIds = originals
            .Select(id => DeterministicGuid.Create($"gje:auto-reversal:{id:N}:{reverseOn:yyyy-MM-dd}"))
            .ToList();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntrySystemReversalRunner>();

            (await runner.PostDueSystemReversalsAsync(reverseOn, batchSize: 2, postedBy: "SYSTEM", ct: CancellationToken.None))
                .Should().Be(2);

            (await runner.PostDueSystemReversalsAsync(reverseOn, batchSize: 2, postedBy: "SYSTEM", ct: CancellationToken.None))
                .Should().Be(1);

            (await runner.PostDueSystemReversalsAsync(reverseOn, batchSize: 2, postedBy: "SYSTEM", ct: CancellationToken.None))
                .Should().Be(0);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

            foreach (var id in reversalIds)
            {
                var d = await docs.GetAsync(id, CancellationToken.None);
                d.Should().NotBeNull();
                d!.Status.Should().Be(DocumentStatus.Posted);
            }
        }
    }

    [Fact]
    public async Task PostDueSystemReversalsAsync_ContinuesWhenOneCandidateFails_PostsOthers_AndRetriesLater()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var reverseOn = new DateOnly(2026, 02, 10);
        var docDateUtc = new DateTime(2026, 02, 01, 12, 0, 0, DateTimeKind.Utc);

        var originals = new List<Guid>();
        for (var i = 0; i < 3; i++)
            originals.Add(await CreateAndPostAutoReverseOriginalAsync(host, docDateUtc.AddDays(i), cashId, revenueId, amount: 10m + i, reverseOn));

        var reversalIds = originals
            .Select(id => DeterministicGuid.Create($"gje:auto-reversal:{id:N}:{reverseOn:yyyy-MM-dd}"))
            .ToList();

        var sabotaged = reversalIds[1];
        await DeleteOneLineAsync(Fixture.ConnectionString, sabotaged);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntrySystemReversalRunner>();

            (await runner.PostDueSystemReversalsAsync(reverseOn, batchSize: 50, postedBy: "SYSTEM", ct: CancellationToken.None))
                .Should().Be(2);

            // Only sabotaged remains due and should keep failing; the runner must swallow the exception and return 0.
            (await runner.PostDueSystemReversalsAsync(reverseOn, batchSize: 50, postedBy: "SYSTEM", ct: CancellationToken.None))
                .Should().Be(0);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

            foreach (var id in reversalIds.Where(x => x != sabotaged))
            {
                (await docs.GetAsync(id, CancellationToken.None))!.Status.Should().Be(DocumentStatus.Posted);
            }

            (await docs.GetAsync(sabotaged, CancellationToken.None))!.Status.Should().Be(DocumentStatus.Draft);
        }
    }

    [Fact]
    public async Task PostDueSystemReversalsAsync_WhenEarliestCandidatesFail_DoesNotStarveLaterCandidates()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var reverseOn = new DateOnly(2026, 02, 10);
        var docDateUtc = new DateTime(2026, 02, 01, 12, 0, 0, DateTimeKind.Utc);

        var originals = new List<Guid>();
        for (var i = 0; i < 4; i++)
            originals.Add(await CreateAndPostAutoReverseOriginalAsync(host, docDateUtc.AddDays(i), cashId, revenueId, amount: 10m + i, reverseOn));

        var reversalIds = originals
            .Select(id => DeterministicGuid.Create($"gje:auto-reversal:{id:N}:{reverseOn:yyyy-MM-dd}"))
            .ToList();

        await DeleteOneLineAsync(Fixture.ConnectionString, reversalIds[0]);
        await DeleteOneLineAsync(Fixture.ConnectionString, reversalIds[1]);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntrySystemReversalRunner>();

            (await runner.PostDueSystemReversalsAsync(reverseOn, batchSize: 2, postedBy: "SYSTEM", ct: CancellationToken.None))
                .Should().Be(2, "failed head-of-queue candidates must not block later due reversals in the same bounded run");
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

            (await docs.GetAsync(reversalIds[0], CancellationToken.None))!.Status.Should().Be(DocumentStatus.Draft);
            (await docs.GetAsync(reversalIds[1], CancellationToken.None))!.Status.Should().Be(DocumentStatus.Draft);
            (await docs.GetAsync(reversalIds[2], CancellationToken.None))!.Status.Should().Be(DocumentStatus.Posted);
            (await docs.GetAsync(reversalIds[3], CancellationToken.None))!.Status.Should().Be(DocumentStatus.Posted);
        }
    }

    [Fact]
    public async Task PostDueSystemReversalsAsync_WhenCancelled_ThrowsAndDoesNotPostAnything()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var reverseOn = new DateOnly(2026, 02, 10);
        var docDateUtc = new DateTime(2026, 02, 01, 12, 0, 0, DateTimeKind.Utc);

        var original = await CreateAndPostAutoReverseOriginalAsync(host, docDateUtc, cashId, revenueId, amount: 10m, reverseOn);
        var reversalId = DeterministicGuid.Create($"gje:auto-reversal:{original:N}:{reverseOn:yyyy-MM-dd}");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntrySystemReversalRunner>();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await FluentActions.Awaiting(() =>
                    runner.PostDueSystemReversalsAsync(reverseOn, batchSize: 50, postedBy: "SYSTEM", ct: cts.Token))
                .Should().ThrowAsync<OperationCanceledException>();
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            (await docs.GetAsync(reversalId, CancellationToken.None))!.Status.Should().Be(DocumentStatus.Draft);
        }
    }

    private static async Task<Guid> CreateAndPostAutoReverseOriginalAsync(
        IHost host,
        DateTime docDateUtc,
        Guid cashId,
        Guid revenueId,
        decimal amount,
        DateOnly reverseOn)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();

        var id = await gje.CreateDraftAsync(docDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

        await gje.UpdateDraftHeaderAsync(
            id,
            new GeneralJournalEntryDraftHeaderUpdate(
                JournalType: null,
                ReasonCode: "AUTO_REV",
                Memo: $"Auto reversal seed {id:N}",
                ExternalReference: null,
                AutoReverse: true,
                AutoReverseOnUtc: reverseOn),
            updatedBy: "u1",
            ct: CancellationToken.None);

        await gje.ReplaceDraftLinesAsync(
            id,
            [
                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Debit,
                    AccountId: cashId,
                    Amount: amount,
                    Memo: null),
                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Credit,
                    AccountId: revenueId,
                    Amount: amount,
                    Memo: null)
            ],
            updatedBy: "u1",
            ct: CancellationToken.None);

        await gje.SubmitAsync(id, submittedBy: "u1", ct: CancellationToken.None);
        await gje.ApproveAsync(id, approvedBy: "u2", ct: CancellationToken.None);
        await gje.PostApprovedAsync(id, postedBy: "u2", ct: CancellationToken.None);

        return id;
    }

    private static async Task DeleteOneLineAsync(string connectionString, Guid documentId)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM doc_general_journal_entry__lines WHERE document_id = @d AND line_no = 1;",
            conn);
        cmd.Parameters.AddWithValue("d", documentId);

        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
