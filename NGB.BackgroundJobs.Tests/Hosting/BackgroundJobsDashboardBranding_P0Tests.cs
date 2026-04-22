using FluentAssertions;
using NGB.BackgroundJobs.Hosting;

namespace NGB.BackgroundJobs.Tests.Hosting;

public sealed class BackgroundJobsDashboardBranding_P0Tests
{
    [Fact]
    public void BuildInlineStyles_Replaces_Brand_Subtitle_Token()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"ngb-bgjobs-branding-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var stylesheetPath = Path.Combine(tempRoot, "hangfire-dashboard.css");
            File.WriteAllText(stylesheetPath, ".navbar-brand::after{content:\"__NGB_DASHBOARD_BRAND_SUBTITLE__\";}");

            var options = new BackgroundJobsHostingOptions
            {
                DashboardBrandSubtitle = "Background Jobs"
            };

            var inlineStyles = BackgroundJobsDashboardBranding.BuildInlineStyles(tempRoot, options);

            inlineStyles.Should().Contain("Background Jobs");
            inlineStyles.Should().NotContain("__NGB_DASHBOARD_BRAND_SUBTITLE__");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void InjectBranding_Inserts_Favicon_And_Style_Before_Head_Close()
    {
        const string html = "<html><head><title>NGB</title></head><body><div>Dashboard</div></body></html>";

        var result = BackgroundJobsDashboardBranding.InjectBranding(html, "body{background:#fff;}", "data:image/svg+xml;base64,AAA=");

        result.Should().Contain("id=\"ngb-standalone-theme\"");
        result.Should().Contain("id=\"ngb-background-jobs-dashboard-favicon\"");
        result.Should().Contain("<style id=\"ngb-background-jobs-dashboard-theme\">body{background:#fff;}</style></head>");
    }
}
