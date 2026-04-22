using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class CloseFiscalYear_Idempotency_DocumentIdIndependenceFromRetainedEarnings_P1Tests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task CloseFiscalYear_CalledTwice_WithDifferentRetainedEarnings_ThrowsExplicitMismatch_AndUsesSameDeterministicDocumentId()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        // Arrange: accounts + one income movement in Jan, then close Jan.
        Guid retainedEarnings1;
        Guid retainedEarnings2;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            // Cash (asset)
            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "50",
                Name: "Cash",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow
            ), CancellationToken.None);

            // Income
            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "70",
                Name: "Rental Income",
                Type: AccountType.Income,
                StatementSection: StatementSection.Income,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow
            ), CancellationToken.None);

            // Retained earnings #1
            retainedEarnings1 = await accounts.CreateAsync(new CreateAccountRequest(
                Code: "303",
                Name: "Retained Earnings (A)",
                Type: AccountType.Equity,
                StatementSection: StatementSection.Equity,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow
            ), CancellationToken.None);

            // Retained earnings #2
            retainedEarnings2 = await accounts.CreateAsync(new CreateAccountRequest(
                Code: "304",
                Name: "Retained Earnings (B)",
                Type: AccountType.Equity,
                StatementSection: StatementSection.Equity,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow
            ), CancellationToken.None);
        }

        // Post one income movement into Jan 2026 (debit cash, credit income).
        var janPostingDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            var documentId = Guid.CreateVersion7();
            await engine.PostAsync(
                PostingOperation.Post,
                async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(documentId, janPostingDate, chart.Get("50"), chart.Get("70"), 100m);
                },
                manageTransaction: true,
                ct: CancellationToken.None);
        }

        // Close January 2026 (required before fiscal-year close into February).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var periodClosing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            await periodClosing.CloseMonthAsync(new DateOnly(2026, 1, 1), closedBy: "tests", CancellationToken.None);
        }

        var fiscalYearEndPeriod = new DateOnly(2026, 2, 1);
        var expectedCloseDocumentId = DeterministicGuid.Create($"CloseFiscalYear|{fiscalYearEndPeriod:yyyy-MM-dd}");

        // Act #1: close fiscal year using retained earnings #1.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var fiscalYearClosing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            await fiscalYearClosing.CloseFiscalYearAsync(fiscalYearEndPeriod, retainedEarnings1, "test", CancellationToken.None);
        }

        // Act #2: close fiscal year again using retained earnings #2.
        Func<Task> act2 = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var fiscalYearClosing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            await fiscalYearClosing.CloseFiscalYearAsync(fiscalYearEndPeriod, retainedEarnings2, "test", CancellationToken.None);
        };

        // Assert: second call is rejected with an explicit retained earnings mismatch, and deterministic document id is the same.
        var ex = await act2.Should().ThrowAsync<FiscalYearAlreadyClosedWithDifferentRetainedEarningsException>();
        ex.Which.ActualRetainedEarningsAccountId.Should().Be(retainedEarnings1);
        ex.Which.RequestedRetainedEarningsAccountId.Should().Be(retainedEarnings2);
        ex.Which.Message.Should().Contain(expectedCloseDocumentId.ToString());

        // Assert: only one posting log exists for the deterministic doc id.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IPostingStateReader>();

            var page = await reader.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = DateTime.UtcNow.AddDays(-7),
                ToUtc = DateTime.UtcNow.AddDays(7),
                DocumentId = expectedCloseDocumentId,
                Operation = PostingOperation.CloseFiscalYear,
                PageSize = 10
            }, CancellationToken.None);

            page.Records.Should().HaveCount(1);
            page.Records[0].Status.Should().Be(PostingStateStatus.Completed);
        }
    }
}
