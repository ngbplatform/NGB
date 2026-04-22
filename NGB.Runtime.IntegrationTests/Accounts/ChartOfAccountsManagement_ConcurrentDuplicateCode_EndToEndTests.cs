using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Persistence.Accounts;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Accounts;

[Collection(PostgresCollection.Name)]
public sealed class ChartOfAccountsManagement_ConcurrentDuplicateCode_EndToEndTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ConcurrentCreateSameCode_OneSucceeds_OtherFails_AndOnlyOneNotDeletedRowExists()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = CreateHost();

        // We intentionally avoid using 50/90.1 to not depend on any other test's conventions.
        var request = new CreateAccountRequest(
            Code: "51",
            Name: "Bank",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow);

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<(bool Ok, Exception? Error, Guid Id)> TryCreateAsync()
        {
            await start.Task;

            await using var scope = host.Services.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            try
            {
                var id = await svc.CreateAsync(request, CancellationToken.None);
                return (true, null, id);
            }
            catch (Exception ex)
            {
                return (false, ex, Guid.Empty);
            }
        }

        var t1 = TryCreateAsync();
        var t2 = TryCreateAsync();
        start.SetResult();

        var results = await Task.WhenAll(t1, t2);

        results.Count(r => r.Ok).Should().Be(1, "unique constraint must allow only one active/not-deleted account per normalized code");
        results.Count(r => !r.Ok).Should().Be(1);

        var failure = results.Single(r => !r.Ok).Error;
        failure.Should().NotBeNull();

        // PostgreSQL unique-violation is 23505.
        failure.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be("23505");

        // Assert: exactly one NOT-deleted row exists for the code.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
            var all = await repo.GetForAdminAsync(includeDeleted: true, CancellationToken.None);

            all.Count(a => a.Account.Code == "51" && !a.IsDeleted).Should().Be(1);
        }
    }

    private IHost CreateHost() => IntegrationHostFactory.Create(Fixture.ConnectionString);
}
