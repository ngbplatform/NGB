using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Accounts;

[Collection(PlatformPostgresCollection.Name)]
public sealed class ChartOfAccountsProvider_IndependentHosts_Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData("create")]
    [InlineData("rename")]
    [InlineData("deactivate")]
    [InlineData("delete")]
    public async Task NewRequestOnAnotherHost_ObservesCommittedAccountChanges(string mutation)
    {
        using var hostA = IntegrationHostFactory.Create(Fixture.ConnectionString);
        using var hostB = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(hostA);
        await using var existingRequest = hostB.Services.CreateAsyncScope();
        var existingProvider = existingRequest.ServiceProvider.GetRequiredService<IChartOfAccountsProvider>();
        var original = await existingProvider.GetAsync();
        original.Get("50").Name.Should().Be("Cash");

        await using (var mutationScope = hostA.Services.CreateAsyncScope())
        {
            var management = mutationScope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            switch (mutation)
            {
                case "create":
                    await management.CreateAsync(new CreateAccountRequest("51", "Bank", AccountType.Asset));
                    break;
                case "rename":
                    await management.UpdateAsync(new UpdateAccountRequest(cashId, Name: "Renamed cash"));
                    break;
                case "deactivate":
                    await management.SetActiveAsync(cashId, false);
                    break;
                case "delete":
                    await management.MarkForDeletionAsync(cashId);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation));
            }
        }

        await using var freshRequest = hostB.Services.CreateAsyncScope();
        var current = await freshRequest.ServiceProvider.GetRequiredService<IChartOfAccountsProvider>().GetAsync();
        if (mutation == "create")
            current.Get("51").Name.Should().Be("Bank");
        else if (mutation == "rename")
            current.Get("50").Name.Should().Be("Renamed cash");
        else
        {
            Action lookup = () => current.Get("50");
            lookup.Should().Throw<AccountNotFoundException>();
            Func<Task> post = () => ReportingTestHelpers.PostAsync(
                hostB, Guid.NewGuid(), ReportingTestHelpers.Day15Utc, "50", "90.1", 100m);
            await post.Should().ThrowAsync<AccountNotFoundException>();
        }

        (await existingProvider.GetAsync()).Should().BeSameAs(original, "an existing request keeps its scoped snapshot");
        original.Get("50").Name.Should().Be("Cash");
    }
}
