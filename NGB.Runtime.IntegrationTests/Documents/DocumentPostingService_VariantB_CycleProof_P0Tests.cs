using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Persistence.Documents;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Workflow;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentPostingService_VariantB_CycleProof_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task PostUnpostPost_RealCycle_WritesFreshSecondPost_AndPreservesAppendOnlyHistory()
    {
        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);

        var dateUtc = new DateTime(2026, 1, 18, 12, 0, 0, DateTimeKind.Utc);
        var period = new DateOnly(2026, 1, 1);
        var docId = await CreateDraftAsync(host, dateUtc, number: "INV-VB-CYCLE-1");

        await PostCashRevenueAsync(host, docId, dateUtc, amount: 100m);
        await UnpostAsync(host, docId);
        await PostCashRevenueAsync(host, docId, dateUtc, amount: 250m);

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var doc = await sp.GetRequiredService<IDocumentRepository>().GetAsync(docId, CancellationToken.None);
        doc.Should().NotBeNull();
        doc!.Status.Should().Be(DocumentStatus.Posted);
        doc.PostedAtUtc.Should().NotBeNull();

        var entries = await sp.GetRequiredService<IAccountingEntryReader>().GetByDocumentAsync(docId, CancellationToken.None);
        entries.Should().HaveCount(3);
        entries.Count(x => x.IsStorno).Should().Be(1);

        var tb = await sp.GetRequiredService<ITrialBalanceReader>()
            .GetAsync(fromInclusive: period, toInclusive: period, CancellationToken.None);

        tb.Single(r => r.AccountCode == "50").ClosingBalance.Should().Be(250m);
        tb.Single(r => r.AccountCode == "90.1").ClosingBalance.Should().Be(-250m);

        var history = await ReadHistoryCountsAsync(sp.GetRequiredService<IUnitOfWork>(), docId);
        history.DocumentPostCompleted.Should().Be(2);
        history.DocumentUnpostCompleted.Should().Be(1);
        history.AccountingPostCompleted.Should().Be(2);
        history.AccountingUnpostCompleted.Should().Be(1);
    }

    [Fact]
    public async Task PostUnpostThenRepost_WhenDocumentIsDraft_FailsFast_AndDoesNotAppendRepostHistory()
    {
        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);

        var dateUtc = new DateTime(2026, 1, 19, 12, 0, 0, DateTimeKind.Utc);
        var period = new DateOnly(2026, 1, 1);
        var docId = await CreateDraftAsync(host, dateUtc, number: "INV-VB-CYCLE-2");

        await PostCashRevenueAsync(host, docId, dateUtc, amount: 100m);
        await UnpostAsync(host, docId);

        Func<Task> act = () => RepostCashRevenueAsync(host, docId, dateUtc, newAmount: 250m);
        await act.Should().ThrowAsync<DocumentWorkflowStateMismatchException>();

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var doc = await sp.GetRequiredService<IDocumentRepository>().GetAsync(docId, CancellationToken.None);
        doc.Should().NotBeNull();
        doc!.Status.Should().Be(DocumentStatus.Draft);
        doc.PostedAtUtc.Should().BeNull();

        var entries = await sp.GetRequiredService<IAccountingEntryReader>().GetByDocumentAsync(docId, CancellationToken.None);
        entries.Should().HaveCount(2);
        entries.Count(x => x.IsStorno).Should().Be(1);

        var tb = await sp.GetRequiredService<ITrialBalanceReader>()
            .GetAsync(fromInclusive: period, toInclusive: period, CancellationToken.None);

        tb.Single(r => r.AccountCode == "50").ClosingBalance.Should().Be(0m);
        tb.Single(r => r.AccountCode == "90.1").ClosingBalance.Should().Be(0m);

        var history = await ReadHistoryCountsAsync(sp.GetRequiredService<IUnitOfWork>(), docId);
        history.DocumentRepostCompleted.Should().Be(0);
        history.AccountingRepostCompleted.Should().Be(0);
    }

    private IHost CreateHost() => IntegrationHostFactory.Create(
        Fixture.ConnectionString,
        services => services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>());

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
            typeCode: "demo.sales_invoice",
            number: number,
            dateUtc: dateUtc,
            manageTransaction: true,
            ct: CancellationToken.None);
    }

    private static async Task PostCashRevenueAsync(IHost host, Guid documentId, DateTime dateUtc, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

        await docs.PostAsync(documentId, async (ctx, ct) =>
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);
            ctx.Post(documentId, dateUtc, chart.Get("50"), chart.Get("90.1"), amount);
        }, CancellationToken.None);
    }

    private static async Task UnpostAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
        await docs.UnpostAsync(documentId, CancellationToken.None);
    }

    private static async Task RepostCashRevenueAsync(IHost host, Guid documentId, DateTime dateUtc, decimal newAmount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

        await docs.RepostAsync(documentId, async (ctx, ct) =>
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);
            ctx.Post(documentId, dateUtc, chart.Get("50"), chart.Get("90.1"), newAmount);
        }, CancellationToken.None);
    }

    private static async Task<HistoryCounts> ReadHistoryCountsAsync(IUnitOfWork uow, Guid documentId)
    {
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        const string sql = """
SELECT
    COALESCE(SUM(CASE WHEN operation = 1 AND event_kind = 2 THEN 1 ELSE 0 END), 0) AS DocumentPostCompleted,
    COALESCE(SUM(CASE WHEN operation = 2 AND event_kind = 2 THEN 1 ELSE 0 END), 0) AS DocumentUnpostCompleted,
    COALESCE(SUM(CASE WHEN operation = 3 AND event_kind = 2 THEN 1 ELSE 0 END), 0) AS DocumentRepostCompleted
FROM platform_document_operation_history
WHERE document_id = @document_id;
""";

        const string accountingSql = """
SELECT
    COALESCE(SUM(CASE WHEN operation = 1 AND event_kind = 2 THEN 1 ELSE 0 END), 0) AS AccountingPostCompleted,
    COALESCE(SUM(CASE WHEN operation = 2 AND event_kind = 2 THEN 1 ELSE 0 END), 0) AS AccountingUnpostCompleted,
    COALESCE(SUM(CASE WHEN operation = 3 AND event_kind = 2 THEN 1 ELSE 0 END), 0) AS AccountingRepostCompleted
FROM accounting_posting_log_history
WHERE document_id = @document_id;
""";

        var document = await uow.Connection.QuerySingleAsync<HistoryCounts>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: CancellationToken.None));

        var accounting = await uow.Connection.QuerySingleAsync<AccountingHistoryCounts>(
            new CommandDefinition(accountingSql, new { document_id = documentId }, uow.Transaction, cancellationToken: CancellationToken.None));

        document.AccountingPostCompleted = accounting.AccountingPostCompleted;
        document.AccountingUnpostCompleted = accounting.AccountingUnpostCompleted;
        document.AccountingRepostCompleted = accounting.AccountingRepostCompleted;
        return document;
    }

    private sealed class HistoryCounts
    {
        public int DocumentPostCompleted { get; set; }
        public int DocumentUnpostCompleted { get; set; }
        public int DocumentRepostCompleted { get; set; }
        public int AccountingPostCompleted { get; set; }
        public int AccountingUnpostCompleted { get; set; }
        public int AccountingRepostCompleted { get; set; }
    }

    private sealed class AccountingHistoryCounts
    {
        public int AccountingPostCompleted { get; set; }
        public int AccountingUnpostCompleted { get; set; }
        public int AccountingRepostCompleted { get; set; }
    }
}
