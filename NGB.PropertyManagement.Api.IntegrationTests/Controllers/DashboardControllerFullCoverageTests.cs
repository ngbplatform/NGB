using FluentAssertions;
using Moq;
using NGB.Core.Security;
using NGB.PropertyManagement.Api.Controllers;
using NGB.PropertyManagement.Contracts.Dashboard;
using NGB.PropertyManagement.Definitions;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Controllers;

public sealed class DashboardControllerFullCoverageTests
{
    [Fact]
    public async Task Get_RequiresHomePageViewPermissionAndDelegatesRequest()
    {
        var asOf = new DateOnly(2026, 8, 21);
        using var cancellation = new CancellationTokenSource();
        var expected = new PropertyManagementDashboardResponse(
            asOf,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            [],
            []);
        var access = new Mock<INgbAccessChecker>(MockBehavior.Strict);
        access.Setup(x => x.RequireAsync(
                NgbResourceKinds.Page,
                PropertyManagementSecurityDefaults.HomePage,
                NgbPermissionActions.View,
                cancellation.Token))
            .Returns(Task.CompletedTask);
        var service = new Mock<IPropertyManagementDashboardService>(MockBehavior.Strict);
        service.Setup(x => x.GetAsync(asOf, cancellation.Token)).ReturnsAsync(expected);

        var actual = await new DashboardController(service.Object, access.Object)
            .Get(asOf, cancellation.Token);

        actual.Should().BeSameAs(expected);
        access.VerifyAll();
        service.VerifyAll();
    }
}
