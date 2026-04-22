using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class PostingEngine_Rollback_NoPostingLog_OnChartResolutionFailure_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime PeriodUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Period = DateOnly.FromDateTime(PeriodUtc);

    [Fact]
    public async Task Post_WhenChartGetFails_RollsBackEverything_NoEntriesNoPostingLog()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await SeedMinimalCoAAsync(host);

        var doc = Guid.CreateVersion7();

        Func<Task> act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var engine = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            await engine.PostAsync(
                PostingOperation.Post,
                async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);

                    // This throws inside the unit-of-work transaction.
                    var missing = chart.Get("THIS_CODE_DOES_NOT_EXIST");

                    ctx.Post(
                        doc,
                        PeriodUtc,
                        chart.Get("50"),
                        missing,
                        1m);
                },
                CancellationToken.None);
        };

        var ex = await act.Should().ThrowAsync<AccountNotFoundException>();
        ex.Which.ErrorCode.Should().Be("coa.account.not_found");
        ex.Which.Context["code"].Should().Be("THIS_CODE_DOES_NOT_EXIST");

        await using var verify = host.Services.CreateAsyncScope();
        var sp = verify.ServiceProvider;

        // No posting log record (it is created inside the transaction, so rollback removes it).
        var log = await sp.GetRequiredService<IPostingStateReader>()
            .GetPageAsync(
                new PostingStatePageRequest
                {
                    FromUtc = PeriodUtc,
                    ToUtc = PeriodUtc,
                    PageSize = 10,
                    DocumentId = doc,
                    Operation = PostingOperation.Post
                },
                CancellationToken.None);

        log.Records.Should().BeEmpty();

        // No journal rows either.
        var gj = await sp.GetRequiredService<IGeneralJournalReader>()
            .GetPageAsync(
                new GeneralJournalPageRequest
                {
                    FromInclusive = Period,
                    ToInclusive = Period,
                    PageSize = 10,
                    DocumentId = doc
                },
                CancellationToken.None);

        gj.Lines.Should().BeEmpty();
    }

    private static async Task SeedMinimalCoAAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        async Task EnsureAsync(string code, string name, AccountType type)
        {
            var existing = (await repo.GetForAdminAsync(includeDeleted: true))
                .FirstOrDefault(a => a.Account.Code == code && !a.IsDeleted);

            if (existing is not null)
            {
                if (!existing.IsActive)
                    await svc.SetActiveAsync(existing.Account.Id, true, CancellationToken.None);
                return;
            }

            await svc.CreateAsync(
                new CreateAccountRequest(
                    Code: code,
                    Name: name,
                    Type: type,
                    NegativeBalancePolicy: NegativeBalancePolicy.Allow
                ),
                CancellationToken.None);
        }

        await EnsureAsync("50", "Cash", AccountType.Asset);
        await EnsureAsync("90.1", "Revenue", AccountType.Income);
    }
}
