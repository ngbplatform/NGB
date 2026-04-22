using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.GeneralJournalEntry;

[Collection(PostgresCollection.Name)]
public sealed class GeneralJournalEntryFacade_EndToEnd_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CreateAndPostApprovedAsync_CreatesRelationships_AndPostsDocument()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var dateUtc = new DateTime(2026, 02, 01, 12, 0, 0, DateTimeKind.Utc);

        Guid sourceA;
        Guid sourceB;
        Guid gjeId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            var facade = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryFacade>();

            sourceA = await drafts.CreateDraftAsync(typeCode: "it_alpha", number: null, dateUtc: dateUtc, manageTransaction: true, ct: CancellationToken.None);
            sourceB = await drafts.CreateDraftAsync(typeCode: "it_beta", number: null, dateUtc: dateUtc, manageTransaction: true, ct: CancellationToken.None);

            var header = new GeneralJournalEntryDraftHeaderUpdate(
                JournalType: GeneralJournalEntryModels.JournalType.Standard,
                ReasonCode: "TEST",
                Memo: "Facade composite flow",
                ExternalReference: "EXT-1",
                AutoReverse: false,
                AutoReverseOnUtc: null);

            var lines = new[]
            {
                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Debit,
                    AccountId: cashId,
                    Amount: 100m,
                    Memo: "Debit"),
                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Credit,
                    AccountId: revenueId,
                    Amount: 100m,
                    Memo: "Credit"),
            };

            gjeId = await facade.CreateAndPostApprovedAsync(
                dateUtc,
                header,
                lines,
                initiatedBy: "u1",
                submittedBy: "u1",
                approvedBy: "u1",
                postedBy: "u1",
                ct: CancellationToken.None,
                createdFromDocumentId: sourceA,
                basedOnDocumentIds: [sourceB]);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<NGB.Persistence.Documents.IDocumentRepository>();
            var relationships = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

            var doc = await docs.GetAsync(gjeId, CancellationToken.None);
            doc.Should().NotBeNull();
            doc!.Status.Should().Be(DocumentStatus.Posted);

            var outgoing = await relationships.ListOutgoingAsync(gjeId, CancellationToken.None);
            outgoing.Should().Contain(x => x.RelationshipCodeNorm == "created_from" && x.ToDocumentId == sourceA);
            outgoing.Should().Contain(x => x.RelationshipCodeNorm == "based_on" && x.ToDocumentId == sourceB);

            await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
            await conn.OpenAsync(CancellationToken.None);

            var typed = (int)(await new NpgsqlCommand(
                "SELECT COUNT(*)::int FROM doc_general_journal_entry WHERE document_id = @d",
                conn)
            {
                Parameters = { new("d", gjeId) }
            }.ExecuteScalarAsync())!;

            var reg = (int)(await new NpgsqlCommand(
                "SELECT COUNT(*)::int FROM accounting_register_main WHERE document_id = @d",
                conn)
            {
                Parameters = { new("d", gjeId) }
            }.ExecuteScalarAsync())!;

            typed.Should().Be(1);
            reg.Should().Be(1);
        }
    }

    [Fact]
    public async Task ReversePostedAsync_PostImmediately_WiresThroughFacade_AndCreatesRelationships()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var dateUtc = new DateTime(2026, 02, 02, 12, 0, 0, DateTimeKind.Utc);
        Guid originalId;
        Guid reversalId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var facade = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryFacade>();

            originalId = await facade.CreateAndPostApprovedAsync(
                dateUtc,
                header: new GeneralJournalEntryDraftHeaderUpdate(
                    JournalType: null,
                    ReasonCode: "ORIG",
                    Memo: "Original",
                    ExternalReference: null,
                    AutoReverse: false,
                    AutoReverseOnUtc: null),
                lines: new[]
                {
                    new GeneralJournalEntryDraftLineInput(
                        Side: GeneralJournalEntryModels.LineSide.Debit,
                        AccountId: cashId,
                        Amount: 25m,
                        Memo: null),
                    new GeneralJournalEntryDraftLineInput(
                        Side: GeneralJournalEntryModels.LineSide.Credit,
                        AccountId: revenueId,
                        Amount: 25m,
                        Memo: null),
                },
                initiatedBy: "u1",
                submittedBy: "u1",
                approvedBy: "u2",
                postedBy: "u2",
                ct: CancellationToken.None);

            reversalId = await facade.ReversePostedAsync(
                originalId,
                reversalDateUtc: dateUtc.AddDays(1),
                initiatedBy: "u3",
                postImmediately: true,
                ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<NGB.Persistence.Documents.IDocumentRepository>();
            var relationships = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

            (await docs.GetAsync(reversalId, CancellationToken.None))!.Status.Should().Be(DocumentStatus.Posted);

            var outgoing = await relationships.ListOutgoingAsync(reversalId, CancellationToken.None);
            outgoing.Should().ContainSingle(x => x.RelationshipCodeNorm == "reversal_of" && x.ToDocumentId == originalId);
            outgoing.Should().ContainSingle(x => x.RelationshipCodeNorm == "created_from" && x.ToDocumentId == originalId);

            var incoming = await relationships.ListIncomingAsync(originalId, CancellationToken.None);
            incoming.Should().ContainSingle(x => x.RelationshipCodeNorm == "reversal_of" && x.FromDocumentId == reversalId);
            incoming.Should().ContainSingle(x => x.RelationshipCodeNorm == "created_from" && x.FromDocumentId == reversalId);
        }
    }
}
