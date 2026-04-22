using FluentAssertions;
using NGB.BackgroundJobs.Hosting;

namespace NGB.BackgroundJobs.Tests.Hosting;

public sealed class BackgroundJobsHostingOptions_P0Tests
{
    [Fact]
    public void ValidateAndNormalize_Normalizes_AdminConsole_Callback_And_PublicOrigin()
    {
        var options = new BackgroundJobsHostingOptions
        {
            AdminConsoleCallbackPath = "hangfire/signin-oidc/",
            AdminConsolePublicOrigin = "https://watchdog.pm.local/",
            DashboardBrandSubtitle = " Background Jobs "
        };

        options.DashboardStylesheetPaths.Clear();
        options.DashboardStylesheetPaths.Add(" hangfire-dashboard.css ");
        options.DashboardStylesheetPaths.Add("   ");

        options.ValidateAndNormalize();

        options.AdminConsoleCallbackPath.Should().Be("/hangfire/signin-oidc");
        options.AdminConsolePublicOrigin.Should().Be("https://watchdog.pm.local");
        options.DashboardBrandSubtitle.Should().Be("Background Jobs");
        options.DashboardStylesheetPaths.Should().Equal("hangfire-dashboard.css");
    }
}
