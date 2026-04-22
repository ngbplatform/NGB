using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class PostingValidatorInvariants_EndToEndTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Amount_must_be_positive_rollback_everything()
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

                    ctx.Post(documentId, period, debit, credit, amount: 0m);
                },
                manageTransaction: true,
                ct: CancellationToken.None);
        };

        // Assert
        (await act.Should().ThrowAsync<NgbArgumentInvalidException>())
            .WithMessage("*must be > 0*");

        await AssertNoSideEffectsAsync(host, documentId, periodDate: DateOnly.FromDateTime(period));
    }

    [Fact]
    public async Task Debit_and_credit_must_be_different_rollback_everything()
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
                    var cash = chart.Get("50");

                    ctx.Post(documentId, period, cash, cash, amount: 100m);
                },
                manageTransaction: true,
                ct: CancellationToken.None);
        };

        // Assert
        (await act.Should().ThrowAsync<NgbArgumentInvalidException>())
            .WithMessage("*Debit and Credit accounts must be different*");

        await AssertNoSideEffectsAsync(host, documentId, periodDate: DateOnly.FromDateTime(period));
    }

    [Fact]
    public async Task Entries_must_belong_to_the_same_document_rollback_everything()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        var documentId1 = Guid.CreateVersion7();
        var documentId2 = Guid.CreateVersion7();
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

                    ctx.Post(documentId1, period, debit, credit, amount: 10m);
                    ctx.Post(documentId2, period, debit, credit, amount: 20m);
                },
                manageTransaction: true,
                ct: CancellationToken.None);
        };

        // Assert
        (await act.Should().ThrowAsync<NgbArgumentInvalidException>())
            .WithMessage("*same DocumentId*");

        // PostingEngine uses documentId from the first entry as the idempotency key (posting_log),
        // but we assert that BOTH documents have no register writes.
        await AssertNoSideEffectsAsync(host, documentId1, periodDate: DateOnly.FromDateTime(period));
        await AssertNoSideEffectsAsync(host, documentId2, periodDate: DateOnly.FromDateTime(period));
    }

    [Fact]
    public async Task Entries_must_belong_to_the_same_UTC_day_rollback_everything()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        var documentId = Guid.CreateVersion7();
        var periodDay1 = new DateTime(2026, 1, 5, 23, 0, 0, DateTimeKind.Utc);
        var periodDay2 = new DateTime(2026, 1, 6, 0, 30, 0, DateTimeKind.Utc);

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

                    ctx.Post(documentId, periodDay1, debit, credit, amount: 10m);
                    ctx.Post(documentId, periodDay2, debit, credit, amount: 20m);
                },
                manageTransaction: true,
                ct: CancellationToken.None);
        };

        // Assert
        (await act.Should().ThrowAsync<NgbArgumentInvalidException>())
            .WithMessage("*same UTC day*");

        await AssertNoSideEffectsAsync(host, documentId, periodDate: DateOnly.FromDateTime(periodDay1));
        await AssertNoSideEffectsAsync(host, documentId, periodDate: DateOnly.FromDateTime(periodDay2));
    }

    [Fact]
    public async Task Required_dimension_must_be_set_rollback_everything()
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
                    var ar = chart.Get("62");     // requires first dimension rule
                    var revenue = chart.Get("90.1");

                    ctx.Post(documentId, period, ar, revenue, amount: 100m);
                },
                manageTransaction: true,
                ct: CancellationToken.None);
        };

        // Assert
        (await act.Should().ThrowAsync<NgbArgumentInvalidException>())
            .WithMessage("*requires dimension*");

        await AssertNoSideEffectsAsync(host, documentId, periodDate: DateOnly.FromDateTime(period));
    }

    [Fact]
    public async Task Extra_dimension_must_not_be_set_rollback_everything()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        var documentId = Guid.CreateVersion7();
        var period = new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);
        var someKey = Guid.CreateVersion7();

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
                    var cash = chart.Get("50"); // no dimensions? allowed
                    var revenue = chart.Get("90.1");

                    ctx.Post(
                        documentId,
                        period,
                        cash,
                        revenue,
                        amount: 100m,
                        debitDimensions: new DimensionBag(new[] { new DimensionValue(Guid.CreateVersion7(), someKey) }));
                },
                manageTransaction: true,
                ct: CancellationToken.None);
        };

        // Assert
        (await act.Should().ThrowAsync<NgbArgumentInvalidException>())
            .WithMessage("*does not allow dimensions*");

        await AssertNoSideEffectsAsync(host, documentId, periodDate: DateOnly.FromDateTime(period));
    }

    [Fact]
    public async Task Posting_period_must_be_UTC_and_fail_fast_without_side_effects()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        var documentId = Guid.CreateVersion7();
        var nonUtcPeriod = new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Unspecified);

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

                    // AccountingEntry enforces UTC at assignment time.
                    ctx.Post(documentId, nonUtcPeriod, debit, credit, amount: 100m);
                },
                manageTransaction: true,
                ct: CancellationToken.None);
        };

        // Assert
        (await act.Should().ThrowAsync<Exception>())
            .WithMessage("*UTC*");

        // Fail-fast: must not write anything.
        await AssertNoSideEffectsAsync(host, documentId, periodDate: DateOnly.FromDateTime(nonUtcPeriod));
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
            Code: "62",
            Name: "Accounts Receivable",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            DimensionRules: new[]
            {
                new AccountDimensionRuleRequest("counterparty", true, Ordinal: 10),
            },
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
