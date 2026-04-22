using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.GeneralJournalEntry;

/// <summary>
/// DR-04: GJE must emit document relationships:
/// - created_from / based_on at draft creation (optional)
/// - reversal_of (+ created_from) for system reversals
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GeneralJournalEntry_DocumentRelationships_DR04_P0Tests(PostgresTestFixture fixture)
{
    private PostgresTestFixture Fixture { get; } = fixture;

    [Fact]
    public async Task CreateDraft_WithCreatedFromAndBasedOn_CreatesOutgoingRelationships()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var docDateUtc = new DateTime(2026, 01, 15, 12, 0, 0, DateTimeKind.Utc);

        Guid sourceA;
        Guid sourceB;
        Guid gjeId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();

            sourceA = await drafts.CreateDraftAsync(typeCode: "it_alpha", number: null, dateUtc: docDateUtc, manageTransaction: true, ct: CancellationToken.None);
            sourceB = await drafts.CreateDraftAsync(typeCode: "it_beta", number: null, dateUtc: docDateUtc, manageTransaction: true, ct: CancellationToken.None);

            gjeId = await gje.CreateDraftAsync(
                docDateUtc,
                initiatedBy: "u1",
                ct: CancellationToken.None,
                createdFromDocumentId: sourceA,
                basedOnDocumentIds: new[] { sourceB });
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var relationships = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

            var outgoing = await relationships.ListOutgoingAsync(gjeId, CancellationToken.None);

            outgoing.Should().ContainSingle(x => x.RelationshipCodeNorm == "created_from" && x.ToDocumentId == sourceA);
            outgoing.Should().ContainSingle(x => x.RelationshipCodeNorm == "based_on" && x.ToDocumentId == sourceB);
        }
    }

    [Fact]
    public async Task ReversePosted_CreatesReversalOfAndCreatedFromRelationships()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId) = await EnsureMinimalAccountsAsync(host.Services, CancellationToken.None);

        var docDateUtc = new DateTime(2026, 01, 20, 12, 0, 0, DateTimeKind.Utc);
        Guid originalId;
        Guid reversalId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();

            originalId = await gje.CreateAndPostApprovedAsync(
                docDateUtc,
                new GeneralJournalEntryDraftHeaderUpdate(
                    JournalType: null,
                    ReasonCode: "TEST",
                    Memo: "Original",
                    ExternalReference: null,
                    AutoReverse: false,
                    AutoReverseOnUtc: null),
                [
                    new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Debit, cashId, 100m, null),
                    new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Credit, revenueId, 100m, null)
                ],
                initiatedBy: "u1",
                submittedBy: "u1",
                approvedBy: "u2",
                postedBy: "u3",
                ct: CancellationToken.None);

            reversalId = await gje.ReversePostedAsync(
                originalId,
                reversalDateUtc: docDateUtc.AddDays(1),
                initiatedBy: "u4",
                postImmediately: true,
                ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var relationships = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

            var outgoing = await relationships.ListOutgoingAsync(reversalId, CancellationToken.None);

            outgoing.Should().ContainSingle(x => x.RelationshipCodeNorm == "reversal_of" && x.ToDocumentId == originalId);
            outgoing.Should().ContainSingle(x => x.RelationshipCodeNorm == "created_from" && x.ToDocumentId == originalId);

            var incoming = await relationships.ListIncomingAsync(originalId, CancellationToken.None);
            incoming.Should().ContainSingle(x => x.RelationshipCodeNorm == "reversal_of" && x.FromDocumentId == reversalId);
            incoming.Should().ContainSingle(x => x.RelationshipCodeNorm == "created_from" && x.FromDocumentId == reversalId);
        }
    }

    private static async Task<(Guid cashId, Guid revenueId)> EnsureMinimalAccountsAsync(
        IServiceProvider sp,
        CancellationToken ct)
    {
        // CoA management is scoped; create a scope explicitly in the test helper.
        await using var scope = sp.CreateAsyncScope();
        var chart = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        var cash = await chart.CreateAsync(new CreateAccountRequest(
            Code: "1010",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            IsContra: false,
            NegativeBalancePolicy: NegativeBalancePolicy.Warn,
            DimensionRules: null), ct);

        var revenue = await chart.CreateAsync(new CreateAccountRequest(
            Code: "4010",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            IsContra: false,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow,
            DimensionRules: null), ct);

        return (cash, revenue);
    }
}
