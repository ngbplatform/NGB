using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Runtime.Admin;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Admin;

[Collection(PostgresCollection.Name)]
public sealed class MainMenuService_PlatformContributors_MergeAndOrder_EndToEnd_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetMainMenuAsync_MergesAccountingGroups_AndKeepsPlatformOrdering()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IMainMenuService>();

        var menu = await service.GetMainMenuAsync(CancellationToken.None);

        menu.Groups.Select(x => x.Label).Should().Equal("Accounting", "Setup & Controls");

        var accounting = menu.Groups[0];
        accounting.Icon.Should().Be("calculator");
        accounting.Items.Select(x => x.Code).Should().Equal(
            "general_journal_entry",
            "accounting.trial_balance",
            "accounting.balance_sheet",
            "accounting.income_statement",
            "accounting.statement_of_changes_in_equity",
            "accounting.cash_flow_statement_indirect",
            "accounting.general_journal",
            "accounting.account_card",
            "accounting.general_ledger_aggregated",
            "accounting.ledger.analysis");

        var controls = menu.Groups[1];
        controls.Icon.Should().Be("settings");
        controls.Items.Select(x => x.Code).Should().Equal(
            "chart-of-accounts",
            "accounting.period_closing",
            "accounting.posting_log",
            "accounting.consistency");
    }
}
