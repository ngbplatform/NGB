using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Posting;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Persistence.Accounts;
using NGB.Persistence.Documents;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

[Collection(PostgresCollection.Name)]
public sealed class PostedDocumentHeaderImmutabilityGuard_AllowsUnpostAndRepost_NumberIsStable_P4Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task UnpostAndRepost_MustBeAllowed_ByHeaderImmutabilityTrigger_AndMustNotChangeDocumentNumber()
    {
        using var host = CreateHost();
        await EnsureMinimalAccountsAsync(host);

        var docDateUtc = new DateTime(2026, 05, 10, 12, 0, 0, DateTimeKind.Utc);
        var typeCode = "foo";
        var number = "FOO-2026-000001";

        Guid docId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            docId = await drafts.CreateDraftAsync(typeCode, number, docDateUtc, manageTransaction: true, ct: CancellationToken.None);
        }

        // Post
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            await posting.PostAsync(
                docId,
                postingAction: async (ctx, ct) => await PostOneEntryAsync(ctx, docId, docDateUtc, amount: 10m, ct),
                ct: CancellationToken.None);
        }

        DateTime postedAt1;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var doc = await docs.GetAsync(docId, CancellationToken.None);
            doc.Should().NotBeNull();
            doc!.Status.Should().Be(DocumentStatus.Posted);
            doc.Number.Should().Be(number);
            doc.PostedAtUtc.Should().NotBeNull();
            postedAt1 = doc.PostedAtUtc!.Value;
        }

        // Repost (must update PostedAtUtc but keep Number)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.RepostAsync(
                docId,
                async (ctx, ct) => await PostOneEntryAsync(ctx, docId, docDateUtc, amount: 11m, ct),
                ct: CancellationToken.None);
        }

        DateTime postedAt2;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var doc = await docs.GetAsync(docId, CancellationToken.None);
            doc.Should().NotBeNull();
            doc!.Status.Should().Be(DocumentStatus.Posted);
            doc.Number.Should().Be(number);
            doc.PostedAtUtc.Should().NotBeNull();
            postedAt2 = doc.PostedAtUtc!.Value;
        }

        postedAt2.Should().BeAfter(postedAt1, "repost should touch PostedAtUtc but must be allowed by header immutability trigger");

        // Unpost (must also be allowed and keep Number)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.UnpostAsync(docId, ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var doc = await docs.GetAsync(docId, CancellationToken.None);
            doc.Should().NotBeNull();
            doc!.Status.Should().Be(DocumentStatus.Draft);
            doc.Number.Should().Be(number);
            doc.PostedAtUtc.Should().BeNull();
        }
    }

    private IHost CreateHost() => IntegrationHostFactory.Create(
        Fixture.ConnectionString,
        services => services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>());

    private static async Task EnsureMinimalAccountsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();

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
    }

    private static async Task PostOneEntryAsync(IAccountingPostingContext ctx, Guid documentId, DateTime periodUtc, decimal amount, CancellationToken ct)
    {
        var chart = await ctx.GetChartOfAccountsAsync(ct);
        var cash = chart.Get("50");
        var revenue = chart.Get("90.1");
        ctx.Post(documentId, periodUtc, debit: cash, credit: revenue, amount);
    }
}
