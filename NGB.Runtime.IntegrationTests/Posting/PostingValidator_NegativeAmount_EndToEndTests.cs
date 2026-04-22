using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class PostingValidator_NegativeAmount_EndToEndTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Amount_must_be_positive_when_negative_rollback_everything()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        var documentId = Guid.CreateVersion7();
        var period = new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            await posting.PostAsync(
                operation: PostingOperation.Post,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    var debit = chart.Get("50");
                    var credit = chart.Get("90.1");

                    ctx.Post(documentId, period, debit, credit, amount: -100m);
                },
                manageTransaction: true,
                ct: CancellationToken.None);
        };

        // Assert
        (await act.Should().ThrowAsync<NgbArgumentInvalidException>())
            .WithMessage("*must be > 0*");

        await AssertNoSideEffectsAsync(host, documentId, periodDate: DateOnly.FromDateTime(period));
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Forbid), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);
    }

    private static async Task AssertNoSideEffectsAsync(IHost host, Guid documentId, DateOnly periodDate)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        (await entryReader.GetByDocumentAsync(documentId, CancellationToken.None)).Should().BeEmpty();

        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        (await turnoverReader.GetForPeriodAsync(periodDate, CancellationToken.None)).Should().BeEmpty();

        var logReader = sp.GetRequiredService<IPostingStateReader>();
        var page = await logReader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddDays(-7),
            ToUtc = DateTime.UtcNow.AddDays(7),
            DocumentId = documentId,
            Operation = PostingOperation.Post,
            PageSize = 50,
        }, CancellationToken.None);

        page.Records.Should().BeEmpty("posting_log must rollback with the transaction");
    }
}
