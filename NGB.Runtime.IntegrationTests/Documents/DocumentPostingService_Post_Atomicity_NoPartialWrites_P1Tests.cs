using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Documents;
using NGB.Accounting.Posting.Validators;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.Accounts;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentPostingService_Post_Atomicity_NoPartialWrites_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task PostAsync_WhenPostingActionThrows_RollsBack_NoEntries_NoPostingLog_DocumentStaysDraft()
    {
        using var host = CreateHost(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        var dateUtc = new DateTime(2026, 1, 20, 12, 0, 0, DateTimeKind.Utc);
        var docId = await CreateDraftAsync(host, dateUtc, number: "LOG-POST-THROW");

        var now = DateTime.UtcNow;
        Func<Task> act = () => PostThenThrowAsync(host, docId, dateUtc, amount: 1m);

        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("boom*");

        await using var scope = host.Services.CreateAsyncScope();

        var doc = await scope.ServiceProvider.GetRequiredService<IDocumentRepository>()
            .GetAsync(docId, CancellationToken.None);

        doc!.Status.Should().Be(DocumentStatus.Draft);

        var entries = await scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>()
            .GetByDocumentAsync(docId, CancellationToken.None);

        entries.Should().BeEmpty();

        var postingLog = scope.ServiceProvider.GetRequiredService<IPostingStateReader>();
        var page = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = now.AddMinutes(-5),
            ToUtc = now.AddMinutes(5),
            DocumentId = docId,
            Operation = PostingOperation.Post,
            PageSize = 50
        }, CancellationToken.None);

        page.Records.Should().BeEmpty();
    }

    private static IHost CreateHost(string connectionString)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNgbRuntime();
                services.AddNgbPostgres(connectionString);

                // PostingEngine requires the accounting validator even if this test focuses on documents.
                services.AddScoped<IAccountingPostingValidator, BasicAccountingPostingValidator>();
            })
            .UseDefaultServiceProvider(options =>
            {
                options.ValidateOnBuild = false;
                options.ValidateScopes = true;
            })
            .Build();
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }

    private static async Task<Guid> CreateDraftAsync(IHost host, DateTime dateUtc, string number)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        return await drafts.CreateDraftAsync(
            typeCode: AccountingDocumentTypeCodes.GeneralJournalEntry,
            number: number,
            dateUtc: dateUtc,
            manageTransaction: true,
            ct: CancellationToken.None);
    }

    private static async Task PostThenThrowAsync(IHost host, Guid documentId, DateTime dateUtc, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

        await docs.PostAsync(documentId, async (ctx, ct) =>
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);

            ctx.Post(
                documentId: documentId,
                period: dateUtc,
                debit: chart.Get("50"),
                credit: chart.Get("90.1"),
                amount: amount);

            throw new NotSupportedException("boom");
        }, CancellationToken.None);
    }
}
