using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentPostingService_VariantB_StrictConflict_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Unpost_WhenAccountingCurrentOperationStateIsAlreadyCompleted_FailsFast_AndStatusRemainsPosted()
    {
        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);

        var dateUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var docId = await CreateDraftAsync(host, dateUtc, number: "INV-VB-1");

        await PostCashRevenueAsync(host, docId, dateUtc, amount: 100m);
        await SeedCompletedAccountingLogAsync(host, docId, PostingOperation.Unpost);

        Func<Task> act = () => UnpostAsync(host, docId);
        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*inconsistent*");

        await using var scope = host.Services.CreateAsyncScope();
        var doc = await scope.ServiceProvider.GetRequiredService<IDocumentRepository>().GetAsync(docId, CancellationToken.None);
        doc!.Status.Should().Be(DocumentStatus.Posted);
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

    private static async Task SeedCompletedAccountingLogAsync(IHost host, Guid documentId, PostingOperation operation)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await uow.EnsureConnectionOpenAsync(ct);
            var cmd = new CommandDefinition(
                """
                INSERT INTO accounting_posting_state (document_id, operation, attempt_id, started_at_utc, completed_at_utc)
                VALUES (@DocumentId, @Operation, @AttemptId, @StartedAtUtc, @CompletedAtUtc)
                ON CONFLICT (document_id, operation) DO UPDATE
                SET attempt_id = EXCLUDED.attempt_id,
                    started_at_utc = EXCLUDED.started_at_utc,
                    completed_at_utc = EXCLUDED.completed_at_utc;
                """,
                new
                {
                    DocumentId = documentId,
                    Operation = (short)operation,
                    AttemptId = Guid.CreateVersion7(),
                    StartedAtUtc = DateTime.UtcNow.AddMinutes(-1),
                    CompletedAtUtc = DateTime.UtcNow
                },
                transaction: uow.Transaction,
                cancellationToken: ct);

            await uow.Connection.ExecuteAsync(cmd);
        }, CancellationToken.None);
    }
}
