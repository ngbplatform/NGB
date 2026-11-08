using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NGB.Core.Locks;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.DependencyInjection;
using NGB.PostgreSql.Locks;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

[Collection(RegistersPostgresCollection.Name)]
public sealed class OperationalRegisterWrite_LockOrdering_Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoRegisters_CompetingWithWriterOrFinalization_MustNotDeadlock(bool finalizeSecond)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
            services.Configure<PostgresOptions>(options => options.AdvisoryLockWaitTimeoutSeconds = 2));
        Guid registerA;
        Guid registerB;
        Guid documentA;
        Guid documentB;
        var date = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var management = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
            var registers = new[]
            {
                await management.UpsertAsync("review_lock_a", "A"),
                await management.UpsertAsync("review_lock_b", "B")
            }.OrderBy(id => id).ToArray();
            registerA = registers[0];
            registerB = registers[1];
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            documentA = await drafts.CreateDraftAsync("test_doc", "A", date);
            documentB = await drafts.CreateDraftAsync("test_doc", "B", date);
        }

        var acquiredB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumeB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task PauseAfterRegisterLockAsync(Task acquisition)
        {
            await acquisition;
            acquiredB.TrySetResult();
            await resumeB.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        using var secondHost = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            services.Configure<PostgresOptions>(options => options.AdvisoryLockWaitTimeoutSeconds = 2);
            services.AddScoped<IAdvisoryLockManager>(sp => ObservingProxy<IOperationalRegisterSharedPeriodLockManager>.Wrap(
                CreateLockManager(sp),
                (method, _, result) => method.Name == nameof(IAdvisoryLockManager.LockOperationalRegisterAsync)
                    ? PauseAfterRegisterLockAsync((Task)result!)
                    : result));
        });

        await using var first = host.Services.CreateAsyncScope();
        await using var second = secondHost.Services.CreateAsyncScope();
        var firstUow = first.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var secondUow = second.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await firstUow.BeginTransactionAsync();
        await secondUow.BeginTransactionAsync();
        var firstWriter = first.ServiceProvider.GetRequiredService<IOperationalRegisterWriteEngine>();
        var secondWriter = second.ServiceProvider.GetRequiredService<IOperationalRegisterWriteEngine>();
        var months = new[] { new DateOnly(2026, 1, 1) };

        try
        {
            await firstWriter.ExecuteAsync(registerA, documentA, OperationalRegisterWriteOperation.Post,
                months, _ => Task.CompletedTask, manageTransaction: false);
            var secondWrite = Record.ExceptionAsync(async () =>
            {
                if (finalizeSecond)
                {
                    await second.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationService>()
                        .MarkDirtyAsync(registerB, months[0], manageTransaction: false);
                }
                else
                {
                    await secondWriter.ExecuteAsync(registerB, documentB, OperationalRegisterWriteOperation.Post,
                        months, _ => Task.CompletedTask, manageTransaction: false);
                }
                await secondUow.CommitAsync();
            });
            await acquiredB.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var firstWrite = Record.ExceptionAsync(async () =>
            {
                await firstWriter.ExecuteAsync(registerB, documentA, OperationalRegisterWriteOperation.Post,
                    months, _ => Task.CompletedTask, manageTransaction: false);
                await firstUow.CommitAsync();
            });
            resumeB.TrySetResult();

            var errors = await Task.WhenAll(firstWrite, secondWrite).WaitAsync(TimeSpan.FromSeconds(10));
            errors.Should().OnlyContain(error => error == null,
                "the canonical write pipeline must not form an ORR(B) / ORP(January) wait cycle");
        }
        finally
        {
            resumeB.TrySetResult();
            await firstUow.RollbackAsync();
            await secondUow.RollbackAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndependentRegisters_ShareMonthButStillRespectExclusiveMonthGuard(bool publishSecond)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var first = host.Services.CreateAsyncScope();
        await using var second = host.Services.CreateAsyncScope();
        await using var maintenance = host.Services.CreateAsyncScope();
        var firstUow = first.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var secondUow = second.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var maintenanceUow = maintenance.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var firstLocks = (IOperationalRegisterSharedPeriodLockManager)first.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();
        var secondLocks = (IOperationalRegisterSharedPeriodLockManager)second.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();
        var maintenanceLocks = maintenance.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();
        var month = new DateOnly(2026, 1, 1);

        await firstUow.BeginTransactionAsync();
        await secondUow.BeginTransactionAsync();
        await maintenanceUow.BeginTransactionAsync();
        try
        {
            await firstLocks.LockOperationalRegisterAsync(Guid.CreateVersion7());
            await firstLocks.LockOperationalRegisterSharedPeriodsAsync([month]);
            using var publicationDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            if (publishSecond)
            {
                await secondLocks.LockOperationalRegisterSharedPublicationAsync(
                    Guid.CreateVersion7(), month, publicationDeadline.Token);
            }
            else
            {
                await secondLocks.LockOperationalRegisterAsync(Guid.CreateVersion7());
                await secondLocks.LockOperationalRegisterSharedPeriodsAsync([month], publicationDeadline.Token);
            }

            using var blocked = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
            Func<Task> exclusive = () => maintenanceLocks.LockPeriodAsync(
                month, AdvisoryLockPeriodScope.OperationalRegister, blocked.Token);
            await exclusive.Should().ThrowAsync<OperationCanceledException>();
            await firstUow.CommitAsync();
            await secondUow.CommitAsync();
            using var released = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await maintenanceLocks.LockPeriodAsync(month, AdvisoryLockPeriodScope.OperationalRegister, released.Token);
        }
        finally
        {
            await firstUow.RollbackAsync();
            await secondUow.RollbackAsync();
            await maintenanceUow.RollbackAsync();
        }
    }

    private static PostgresAdvisoryLockManager CreateLockManager(IServiceProvider services)
        => new(
            services.GetRequiredService<IUnitOfWork>(),
            services.GetRequiredService<IOptions<PostgresOptions>>(),
            services.GetRequiredService<ILogger<PostgresAdvisoryLockManager>>(),
            TimeProvider.System);
}
