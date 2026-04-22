using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Core.Documents;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Persistence.Accounts;
using NGB.Persistence.Documents;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.GeneralJournalEntry;

[Collection(PostgresCollection.Name)]
public sealed class GeneralJournalEntry_AutoReversal_Numbering_YearBoundary_P4Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AutoReverse_AcrossYearBoundary_NumberingUsesRespectiveYears()
    {
        using var host = CreateHost();
        var (cashId, revenueId) = await EnsureMinimalAccountsAsync(host);

        var originalDateUtc = new DateTime(2025, 12, 31, 23, 0, 0, DateTimeKind.Utc);
        var reverseOn = new DateOnly(2026, 01, 02);

        Guid originalId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            originalId = await gje.CreateDraftAsync(originalDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

            await gje.UpdateDraftHeaderAsync(
                originalId,
                new GeneralJournalEntryDraftHeaderUpdate(
                    JournalType: null,
                    ReasonCode: "ACCRUAL",
                    Memo: "Auto-reversal across year boundary",
                    ExternalReference: null,
                    AutoReverse: true,
                    AutoReverseOnUtc: reverseOn),
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

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var original = await docs.GetAsync(originalId, CancellationToken.None);
            original.Should().NotBeNull();
            original!.Status.Should().Be(DocumentStatus.Posted);
            original.Number.Should().StartWith("GJE-2025-");
            original.Number.Should().EndWith("000001", "DB is reset per test, so this is the first 2025 sequence");
        }

        // Runner does not post early
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntrySystemReversalRunner>();
            (await runner.PostDueSystemReversalsAsync(new DateOnly(2026, 01, 01), batchSize: 50, postedBy: "SYSTEM", ct: CancellationToken.None))
                .Should().Be(0);
        }

        // Runner posts on due date
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntrySystemReversalRunner>();
            (await runner.PostDueSystemReversalsAsync(reverseOn, batchSize: 50, postedBy: "SYSTEM", ct: CancellationToken.None))
                .Should().Be(1);
        }

        var expectedReversalId = NGB.Tools.Extensions.DeterministicGuid.Create($"gje:auto-reversal:{originalId:N}:{reverseOn:yyyy-MM-dd}");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var rev = await docs.GetAsync(expectedReversalId, CancellationToken.None);
            rev.Should().NotBeNull();
            rev!.Status.Should().Be(DocumentStatus.Posted);
            rev.Number.Should().StartWith("GJE-2026-");
            rev.Number.Should().EndWith("000001", "DB is reset per test, so this is the first 2026 sequence");
        }
    }

    private IHost CreateHost() => IntegrationHostFactory.Create(Fixture.ConnectionString);

    private static async Task<(Guid cashId, Guid revenueId)> EnsureMinimalAccountsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();

        // Make helper idempotent. Unique constraint is for not-deleted rows.
        var existing = await repo.GetForAdminAsync(includeDeleted: true, ct: CancellationToken.None);
        static bool HasNotDeleted(IReadOnlyList<ChartOfAccountsAdminItem> items, string code) =>
            items.Any(x => !x.IsDeleted && string.Equals(x.Account.Code, code, StringComparison.OrdinalIgnoreCase));

        if (!HasNotDeleted(existing, "50"))
        {
            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "50",
                Name: "Cash",
                AccountType.Asset,
                StatementSection: StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow
            ), CancellationToken.None);
        }

        if (!HasNotDeleted(existing, "90.1"))
        {
            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "90.1",
                Name: "Revenue",
                AccountType.Income,
                StatementSection: StatementSection.Income,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow
            ), CancellationToken.None);
        }

        // Resolve ids
        var refreshed = await repo.GetForAdminAsync(includeDeleted: false, ct: CancellationToken.None);
        var cashId = refreshed.Single(x => x.Account.Code == "50").Account.Id;
        var revenueId = refreshed.Single(x => x.Account.Code == "90.1").Account.Id;
        return (cashId, revenueId);
    }
}
