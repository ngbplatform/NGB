using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Checkers;
using NGB.Persistence.Periods;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Checkers;
using NGB.PostgreSql.Periods;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using NGB.Runtime.Periods;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PlatformPostgresCollection.Name)]
public sealed class ClosedPeriodChain_Concurrency_Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReopenPreviousAndCloseNext_SerializeChainValidation(bool closeFirst)
    {
        var january = new DateOnly(2026, 1, 1);
        var february = january.AddMonths(1);
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await ReportingTestHelpers.PostAsync(
            host, Guid.NewGuid(), ReportingTestHelpers.Day15Utc, "50", "90.1", 100m);
        await ReportingTestHelpers.CloseMonthAsync(host, january);

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task PauseAsync(Task operation)
        {
            await operation;
            entered.TrySetResult();
            await resume.Task.WaitAsync(TimeSpan.FromSeconds(10));
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
                services.AddScoped<IClosedPeriodRepository>(sp => ObservingProxy<IClosedPeriodRepository>.Wrap(
                    new PostgresClosedPeriodRepository(sp.GetRequiredService<IUnitOfWork>()),
                    (method, _, result) => method.Name == nameof(IClosedPeriodRepository.ReopenAsync)
                        ? PauseAsync((Task)result!)
                        : result));
            }
        });

        await using var firstScope = pausedHost.Services.CreateAsyncScope();
        var firstService = firstScope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
        var first = closeFirst
            ? firstService.CloseMonthAsync(february, "test")
            : firstService.ReopenMonthAsync(january, "test", "Chain concurrency test");

        await using var secondScope = host.Services.CreateAsyncScope();
        var secondService = secondScope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
        Exception? secondError;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            secondError = await Record.ExceptionAsync(() => closeFirst
                ? secondService.ReopenMonthAsync(january, "test", "Chain concurrency test", cancellation.Token)
                : secondService.CloseMonthAsync(february, "test", cancellation.Token));
        }
        finally
        {
            resume.TrySetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(10));
        }

        var periods = secondScope.ServiceProvider.GetRequiredService<IClosedPeriodRepository>();
        var januaryClosed = await periods.IsClosedAsync(january);
        var februaryClosed = await periods.IsClosedAsync(february);
        (februaryClosed && !januaryClosed).Should().BeFalse("concurrent operations must preserve the closed chain");
        secondError.Should().BeAssignableTo<OperationCanceledException>("the competing chain change must wait");
        januaryClosed.Should().Be(closeFirst);
        februaryClosed.Should().Be(closeFirst);

        var retryError = await Record.ExceptionAsync(() => closeFirst
            ? secondService.ReopenMonthAsync(january, "test", "Retry after close")
            : secondService.CloseMonthAsync(february, "test"));
        if (closeFirst)
            retryError.Should().BeOfType<MonthReopenLatestClosedRequiredException>();
        else
            retryError.Should().BeOfType<MonthClosingPrerequisiteNotMetException>();
    }
}
