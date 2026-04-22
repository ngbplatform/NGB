using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Persistence.Accounts;
using NGB.Runtime.Accounts;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Accounts;

public sealed class ChartOfAccountsProvider_ConcurrentGet_LoadsOnce_P1Tests
{
    [Fact]
    public async Task GetAsync_CalledConcurrently_LoadsFromRepositoryOnlyOnce_AndReturnsCachedInstance()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repo = new CountingChartOfAccountsRepository(gate);

        var services = new ServiceCollection();
        services.AddScoped<IChartOfAccountsRepository>(_ => repo);
        services.AddScoped<IChartOfAccountsProvider, ChartOfAccountsProvider>();

        await using var sp = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        await using var scope = sp.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<IChartOfAccountsProvider>();

        var tasks = Enumerable.Range(0, 25)
            .Select(_ => provider.GetAsync(CancellationToken.None))
            .ToArray();

        // Ensure at least one caller entered GetAllAsync and is blocked, so the rest queue behind the semaphore.
        while (repo.GetAllCalls == 0)
            await Task.Delay(1);

        gate.SetResult(true);

        var charts = await Task.WhenAll(tasks);

        repo.GetAllCalls.Should().Be(1);
        charts.Distinct().Should().HaveCount(1, "provider should cache the same chart instance within the scope");
        charts[0].Get("50").Name.Should().Be("Cash");
    }

    private sealed class CountingChartOfAccountsRepository(TaskCompletionSource<bool> gate) : IChartOfAccountsRepository
    {
        private int _getAllCalls;

        public int GetAllCalls => Volatile.Read(ref _getAllCalls);

        public async Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _getAllCalls);
            await gate.Task.WaitAsync(ct);

            return new List<Account>
            {
                new(
                    id: Guid.CreateVersion7(),
                    code: "50",
                    name: "Cash",
                    type: AccountType.Asset,
                    statementSection: StatementSection.Assets,
                    negativeBalancePolicy: NegativeBalancePolicy.Allow)
            };
        }

        public Task<IReadOnlyList<ChartOfAccountsAdminItem>> GetForAdminAsync(bool includeDeleted = false, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ChartOfAccountsAdminItem?> GetAdminByIdAsync(Guid accountId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ChartOfAccountsAdminItem>> GetAdminByIdsAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> HasMovementsAsync(Guid accountId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task CreateAsync(Account account, bool isActive = true, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<string?> GetCodeByIdAsync(Guid accountId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task UpdateAsync(Account account, bool isActive, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task SetActiveAsync(Guid accountId, bool isActive, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task MarkForDeletionAsync(Guid accountId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task UnmarkForDeletionAsync(Guid accountId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
