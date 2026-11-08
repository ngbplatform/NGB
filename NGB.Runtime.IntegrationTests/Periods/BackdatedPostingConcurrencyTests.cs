using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.PostingState;
using NGB.Persistence.Checkers;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.Writers;
using NGB.PostgreSql.Checkers;
using NGB.PostgreSql.Writers;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(AccountingPostgresCollection.Name)]
public sealed class BackdatedPostingConcurrencyTests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EarlierPostAndFirstMonthClose_SerializeBothOrders(bool closeFirst)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await PostAsync(host, 2, CancellationToken.None);

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task PauseAsync(Task operation)
        {
            await operation;
            entered.TrySetResult();
            await resume.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }

        using var pausedHost = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            if (closeFirst)
            {
                services.AddScoped<IAccountingIntegrityChecker>(sp => ObservingProxy<IAccountingIntegrityChecker>.Wrap(
                    new PostgresAccountingIntegrityChecker(sp.GetRequiredService<IUnitOfWork>()),
                    (_, _, result) => PauseAsync((Task)result!)));
            }
            else
            {
                services.AddScoped<IAccountingEntryWriter>(sp => ObservingProxy<IAccountingEntryWriter>.Wrap(
                    new PostgresAccountingEntryWriter(sp.GetRequiredService<IUnitOfWork>()),
                    (_, _, result) => PauseAsync((Task)result!)));
            }
        });

        var first = closeFirst
            ? CloseAsync(pausedHost, CancellationToken.None)
            : PostAsync(pausedHost, 1, CancellationToken.None);
        Exception? competingError;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            competingError = await Record.ExceptionAsync(() => closeFirst
                ? PostAsync(host, 1, cancellation.Token)
                : CloseAsync(host, cancellation.Token));
        }
        finally
        {
            resume.TrySetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(10));
        }

        competingError.Should().BeAssignableTo<OperationCanceledException>("the dependent operation must wait");
        if (closeFirst)
        {
            Func<Task> retry = () => PostAsync(host, 1, CancellationToken.None);
            await retry.Should().ThrowAsync<PostingBeforeClosedPeriodException>();
        }
        else
        {
            Func<Task> retry = () => CloseAsync(host, CancellationToken.None);
            await retry.Should().ThrowAsync<MonthClosingPrerequisiteNotMetException>();
            await ReportingTestHelpers.CloseMonthAsync(host, new DateOnly(2026, 1, 1));
            await CloseAsync(host, CancellationToken.None);
        }

        await using var scope = host.Services.CreateAsyncScope();
        var march = new DateOnly(2026, 3, 1);
        var rows = await scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>().GetAsync(march, march);
        rows.Single(row => row.AccountCode == "50").ClosingBalance.Should().Be(closeFirst ? 100m : 150m);
    }

    [Fact]
    public async Task DirectEarlierInsert_WaitsForConcurrentClose_ThenSeesCommittedClosedMonth()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await PostAsync(host, 2, CancellationToken.None);

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task PauseAsync(Task operation)
        {
            await operation;
            entered.TrySetResult();
            await resume.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }

        using var pausedHost = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
            services.AddScoped<IAccountingIntegrityChecker>(sp => ObservingProxy<IAccountingIntegrityChecker>.Wrap(
                new PostgresAccountingIntegrityChecker(sp.GetRequiredService<IUnitOfWork>()),
                (_, _, result) => PauseAsync((Task)result!))));

        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        var documentId = Guid.CreateVersion7();
        var close = CloseAsync(pausedHost, CancellationToken.None);
        Task<int>? insert = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            insert = connection.ExecuteAsync("""
                INSERT INTO accounting_register_main
                    (document_id, period, debit_account_id, credit_account_id, amount, is_storno)
                VALUES (@DocumentId, '2026-01-15 00:00:00+00', @CashId, @RevenueId, 50, FALSE);
                """, new { DocumentId = documentId, CashId = cashId, RevenueId = revenueId });

            // The SQL statement begins before the close commits. Its trigger must recheck
            // the closed-month boundary after acquiring the shared history lock.
            var completed = await Task.WhenAny(insert, Task.Delay(300));
            completed.Should().NotBeSameAs(insert);
        }
        finally
        {
            resume.TrySetResult();
            await close.WaitAsync(TimeSpan.FromSeconds(10));
        }

        Func<Task> finishInsert = () => insert!.WaitAsync(TimeSpan.FromSeconds(10));
        await finishInsert.Should().ThrowAsync<PostgresException>()
            .WithMessage("*later period is closed*2026-02-01*");
        (await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting_register_main WHERE document_id = @documentId;", new { documentId }))
            .Should().Be(0);
    }

    private static async Task PostAsync(IHost host, int month, CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var documentId = Guid.CreateVersion7();
        await scope.ServiceProvider.GetRequiredService<PostingEngine>().PostAsync(PostingOperation.Post,
            async (context, innerCt) =>
            {
                var chart = await context.GetChartOfAccountsAsync(innerCt);
                context.Post(documentId, new DateTime(2026, month, 15, 0, 0, 0, DateTimeKind.Utc),
                    chart.Get("50"), chart.Get("90.1"), month == 1 ? 50m : 100m);
            }, ct);
    }

    private static async Task CloseAsync(IHost host, CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IPeriodClosingService>()
            .CloseMonthAsync(new DateOnly(2026, 2, 1), "test", ct);
    }
}
