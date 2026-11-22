using EquipmentInspection.Runtime;
using Xunit;

namespace EquipmentInspection.Tests;

public sealed class MainMenuContractTests
{
    [Fact]
    public async Task Cancelled_menu_contribution_does_not_return_navigation()
    {
        var contributor = new EquipmentMainMenuContributor();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            contributor.ContributeAsync(new CancellationToken(true)));
        var groups = await contributor.ContributeAsync(CancellationToken.None);
        Assert.Equal("home", Assert.Single(groups[0].Items).Icon);
        Assert.Equal(3, groups[1].Items.Count);
    }
}
