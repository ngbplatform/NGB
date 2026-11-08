using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.Writers;
using NGB.PostgreSql.Accounts;
using NGB.PostgreSql.Writers;
using NGB.Runtime.Accounts;
using NGB.Runtime.Accounts.Exceptions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Accounts;

[Collection(PlatformPostgresCollection.Name)]
public sealed class AccountDeletion_PostingConcurrency_Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FirstPostingAndDeletion_CannotBothCommit(bool postFirst)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task PauseAsync(Task operation)
        {
            await operation;
            entered.TrySetResult();
            await resume.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        using var pausedHost = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            if (postFirst)
            {
                services.AddScoped<IAccountingEntryWriter>(sp => ObservingProxy<IAccountingEntryWriter>.Wrap(
                    new PostgresAccountingEntryWriter(sp.GetRequiredService<IUnitOfWork>()),
                    (_, _, result) => PauseAsync((Task)result!)));
            }
            else
            {
                services.AddScoped<IChartOfAccountsRepository>(sp => ObservingProxy<IChartOfAccountsLockingRepository>.Wrap(
                    new PostgresChartOfAccountsRepository(sp.GetRequiredService<IUnitOfWork>()),
                    (method, _, result) => method.Name == nameof(IChartOfAccountsRepository.MarkForDeletionAsync)
                        ? PauseAsync((Task)result!)
                        : result));
            }
        });

        var documentId = Guid.NewGuid();
        await using var firstScope = pausedHost.Services.CreateAsyncScope();
        await using var secondScope = host.Services.CreateAsyncScope();
        // Keep the chart that the posting request resolved before the deletion started.
        var postingScope = postFirst ? firstScope : secondScope;
        var chart = await postingScope.ServiceProvider.GetRequiredService<IChartOfAccountsProvider>().GetAsync();
        var posting = postingScope.ServiceProvider.GetRequiredService<PostingEngine>();
        var deletingScope = postFirst ? secondScope : firstScope;
        var deletion = deletingScope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        Task PostAsync(CancellationToken ct) => posting.PostAsync(PostingOperation.Post, (context, _) =>
        {
            context.Post(documentId, ReportingTestHelpers.Day15Utc, chart.Get("50"), chart.Get("90.1"), 100m);
            return Task.CompletedTask;
        }, ct);

        var first = postFirst ? PostAsync(CancellationToken.None) : deletion.MarkForDeletionAsync(cashId);
        Task<Exception?>? second = null;
        var completedBeforeRelease = false;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            second = Record.ExceptionAsync(() => postFirst
                ? deletion.MarkForDeletionAsync(cashId)
                : PostAsync(CancellationToken.None));
            completedBeforeRelease = await Task.WhenAny(second, Task.Delay(500)) == second;
        }
        finally
        {
            resume.TrySetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(10));
        }

        var secondError = await second!.WaitAsync(TimeSpan.FromSeconds(10));
        await using var verify = host.Services.CreateAsyncScope();
        var account = await verify.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>().GetAdminByIdAsync(cashId);
        var rows = await verify.ServiceProvider.GetRequiredService<ITrialBalanceReader>()
            .GetAsync(ReportingTestHelpers.Period, ReportingTestHelpers.Period);
        rows.Sum(row => row.ClosingBalance).Should().Be(0m, "deletion must not hide one side of a committed entry");
        completedBeforeRelease.Should().BeFalse("the conflicting operation must wait for the first transaction");
        account!.IsDeleted.Should().Be(!postFirst);
        if (postFirst)
            secondError.Should().BeOfType<AccountHasMovementsCannotDeleteException>();
        else
            secondError.Should().BeOfType<AccountNotFoundException>();

        var retryError = await Record.ExceptionAsync(() => postFirst
            ? deletion.MarkForDeletionAsync(cashId)
            : PostAsync(CancellationToken.None));
        if (postFirst)
            retryError.Should().BeOfType<AccountHasMovementsCannotDeleteException>();
        else
            retryError.Should().BeOfType<AccountNotFoundException>();

        var entries = await verify.ServiceProvider.GetRequiredService<IAccountingEntryReader>().GetByDocumentAsync(documentId);
        entries.Should().HaveCount(postFirst ? 1 : 0);
    }

    [Fact]
    public async Task PostingsInDifferentMonths_CanShareAccountLocks()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task PauseAsync(Task write)
        {
            await write;
            entered.TrySetResult();
            await resume.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        using var pausedHost = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
            services.AddScoped<IAccountingEntryWriter>(sp => ObservingProxy<IAccountingEntryWriter>.Wrap(
                new PostgresAccountingEntryWriter(sp.GetRequiredService<IUnitOfWork>()),
                (_, _, result) => PauseAsync((Task)result!))));

        var first = ReportingTestHelpers.PostAsync(
            pausedHost, Guid.NewGuid(), ReportingTestHelpers.Day15Utc, "50", "90.1", 100m);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await ReportingTestHelpers.PostAsync(
                    host, Guid.NewGuid(), ReportingTestHelpers.Day15Utc.AddMonths(1), "50", "90.1", 50m)
                .WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            resume.TrySetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}
