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
public sealed class GeneralJournalEntrySystemReversalRunner_Concurrency_P0Tests(PostgresTestFixture fixture)
{
    private PostgresTestFixture Fixture { get; } = fixture;

    [Fact]
    public async Task PostDueSystemReversalsAsync_TwoRunnersConcurrently_DoNotDuplicateRegisterWrites()
    {
        await Fixture.ResetDatabaseAsync();

        var reverseOn = new DateOnly(2026, 02, 10);
        var docDateUtc = new DateTime(2026, 02, 01, 12, 0, 0, DateTimeKind.Utc);

        using var seedHost = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(seedHost);

        var originalId = await CreateAndPostAutoReverseOriginalAsync(seedHost, docDateUtc, cashId, revenueId, amount: 10m, reverseOn);
        var reversalId = DeterministicGuid.Create($"gje:auto-reversal:{originalId:N}:{reverseOn:yyyy-MM-dd}");

        // Sanity: reversal exists and is not posted yet.
        await using (var scope = seedHost.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var rev = await docs.GetAsync(reversalId, CancellationToken.None);
            rev.Should().NotBeNull();
            rev!.Status.Should().Be(DocumentStatus.Draft);
        }

        using var host1 = IntegrationHostFactory.Create(Fixture.ConnectionString);
        using var host2 = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var barrier = new Barrier(2);

        var t1 = Task.Run(async () =>
        {
            await using var scope = host1.Services.CreateAsyncScope();
            var runner = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntrySystemReversalRunner>();
            barrier.SignalAndWait();
            return await runner.PostDueSystemReversalsAsync(reverseOn, batchSize: 50, postedBy: "SYSTEM", ct: CancellationToken.None);
        });

        var t2 = Task.Run(async () =>
        {
            await using var scope = host2.Services.CreateAsyncScope();
            var runner = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntrySystemReversalRunner>();
            barrier.SignalAndWait();
            return await runner.PostDueSystemReversalsAsync(reverseOn, batchSize: 50, postedBy: "SYSTEM", ct: CancellationToken.None);
        });

        await Task.WhenAll(t1, t2);

        // Assert final state: reversal posted exactly once (register rows are not duplicated).
        await using (var scope = host1.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var rev = await docs.GetAsync(reversalId, CancellationToken.None);
            rev.Should().NotBeNull();
            rev!.Status.Should().Be(DocumentStatus.Posted);
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var regCount = (int)(await new NpgsqlCommand(
                "SELECT COUNT(*)::int FROM accounting_register_main WHERE document_id = @d",
                conn)
            {
                Parameters = { new("d", reversalId) }
            }.ExecuteScalarAsync(CancellationToken.None))!;

            regCount.Should().Be(1, "concurrent runners must not double-write accounting movements");
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
                Memo: "Auto reversal seed",
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
}
