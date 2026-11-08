using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Posting;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Posting;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PlatformPostgresCollection.Name)]
public sealed class DocumentPostingService_StoredEffect_Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData(100)]
    [InlineData(250)]
    public async Task UnpostAfterExplicitRepost_CancelsStoredAmount_AndPreservesRepostNoOp(decimal replacementAmount)
    {
        var resolver = new FixedAccountingResolver();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
            services.AddSingleton<IDocumentPostingActionResolver>(resolver));
        await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await using var scope = host.Services.CreateAsyncScope();
        var documentId = await scope.ServiceProvider.GetRequiredService<IDocumentDraftService>()
            .CreateDraftAsync("test_doc", "STORED-EFFECT", ReportingTestHelpers.Day15Utc);
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
        var entries = scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>();
        var trialBalance = scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>();

        await documents.PostAsync(documentId);
        resolver.RejectInvocation = true;
        await documents.RepostAsync(documentId, async (context, ct) =>
        {
            var chart = await context.GetChartOfAccountsAsync(ct);
            context.Post(documentId, ReportingTestHelpers.Day15Utc, chart.Get("50"), chart.Get("90.1"), replacementAmount);
        });
        var replaced = await entries.GetByDocumentAsync(documentId);
        replaced.Should().HaveCount(3);
        var balance = await trialBalance.GetAsync(ReportingTestHelpers.Period, ReportingTestHelpers.Period);
        balance.Single(row => row.AccountCode == "50").ClosingBalance.Should().Be(replacementAmount);

        await documents.RepostAsync(documentId, (_, _) => throw new InvalidOperationException("A repeated Repost must not invoke its handler."));
        (await entries.GetByDocumentAsync(documentId)).Select(entry => entry.EntryId)
            .Should().Equal(replaced.Select(entry => entry.EntryId));

        await documents.UnpostAsync(documentId);
        var document = await scope.ServiceProvider.GetRequiredService<IDocumentRepository>().GetAsync(documentId);
        document!.Status.Should().Be(DocumentStatus.Draft);
        balance = await trialBalance.GetAsync(ReportingTestHelpers.Period, ReportingTestHelpers.Period);
        balance.Single(row => row.AccountCode == "50").ClosingBalance.Should().Be(0m);
        (await entries.GetByDocumentAsync(documentId)).Should().HaveCount(4, "already reversed entries must not be reversed again");

        for (var cycle = 1; cycle <= 2; cycle++)
        {
            await documents.PostAsync(documentId, async (context, ct) =>
            {
                var chart = await context.GetChartOfAccountsAsync(ct);
                context.Post(documentId, ReportingTestHelpers.Day15Utc, chart.Get("50"), chart.Get("90.1"), 300m * cycle);
            });
            await documents.UnpostAsync(documentId);
            balance = await trialBalance.GetAsync(ReportingTestHelpers.Period, ReportingTestHelpers.Period);
            balance.Single(row => row.AccountCode == "50").ClosingBalance.Should().Be(0m);
            (await entries.GetByDocumentAsync(documentId)).Should().HaveCount(4 + cycle * 2);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ZeroEffectDocument_PreservesAccountingTransitionsWithoutGrowingReversalBatches(bool explicitStorno)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await using var scope = host.Services.CreateAsyncScope();
        var documentId = await scope.ServiceProvider.GetRequiredService<IDocumentDraftService>()
            .CreateDraftAsync("test_doc", "ZERO-EFFECT", ReportingTestHelpers.Day15Utc);
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

        for (var cycle = 1; cycle <= 3; cycle++)
        {
            await documents.PostAsync(documentId, async (context, ct) =>
            {
                var chart = await context.GetChartOfAccountsAsync(ct);
                context.Post(documentId, ReportingTestHelpers.Day15Utc, chart.Get("50"), chart.Get("90.1"), 100m);
                context.Post(documentId, ReportingTestHelpers.Day15Utc, chart.Get("90.1"), chart.Get("50"), 100m, isStorno: explicitStorno);
            });
            await documents.UnpostAsync(documentId);

            var entries = await scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>().GetByDocumentAsync(documentId);
            entries.Should().HaveCount(cycle * 4);
            var balance = await scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>()
                .GetAsync(ReportingTestHelpers.Period, ReportingTestHelpers.Period);
            balance.Should().OnlyContain(row => row.ClosingBalance == 0m);
            var document = await scope.ServiceProvider.GetRequiredService<IDocumentRepository>().GetAsync(documentId);
            document!.Status.Should().Be(DocumentStatus.Draft);
        }
    }

    private sealed class FixedAccountingResolver : IDocumentPostingActionResolver
    {
        public bool RejectInvocation { get; set; }

        public Func<IAccountingPostingContext, CancellationToken, Task>? TryResolve(DocumentRecord document)
            => Resolve(document);

        public Func<IAccountingPostingContext, CancellationToken, Task> Resolve(DocumentRecord document)
            => async (context, ct) =>
            {
                if (RejectInvocation)
                    throw new InvalidOperationException("Reversal must not invoke the current posting handler.");

                var chart = await context.GetChartOfAccountsAsync(ct);
                context.Post(document.Id, document.DateUtc, chart.Get("50"), chart.Get("90.1"), 100m);
            };
    }
}
