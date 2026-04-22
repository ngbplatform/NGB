using NGB.Tools.Exceptions;

namespace NGB.Watchdog.Hosting;

public sealed class WatchdogOptions
{
    public string HealthPath { get; set; } = "/health";

    public string UiPath { get; set; } = "/health-ui";

    public string ApiPath { get; set; } = "/health-ui-api";

    public string PageTitle { get; set; } = "NGB: Health";

    public bool AsideMenuOpened { get; set; } = true;

    public bool RequireAuthorization { get; set; } = true;

    public bool MapAccountEndpoints { get; set; } = true;

    public IList<string> CustomStylesheets { get; } = [];

    public WatchdogOptions AddCustomStylesheet(string stylesheetPath)
    {
        if (string.IsNullOrWhiteSpace(stylesheetPath))
            throw new NgbArgumentRequiredException(nameof(stylesheetPath));

        CustomStylesheets.Add(stylesheetPath.Trim());
        return this;
    }

    internal void ValidateAndNormalize()
    {
        HealthPath = NormalizePath(HealthPath, nameof(HealthPath));
        UiPath = NormalizePath(UiPath, nameof(UiPath));
        ApiPath = NormalizePath(ApiPath, nameof(ApiPath));

        if (string.IsNullOrWhiteSpace(PageTitle))
            throw new NgbArgumentRequiredException(nameof(PageTitle));

        PageTitle = PageTitle.Trim();
    }

    private static string NormalizePath(string path, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new NgbArgumentRequiredException(propertyName);

        var normalized = path.Trim();

        if (!normalized.StartsWith('/'))
            normalized = $"/{normalized}";

        if (normalized.Length > 1)
            normalized = normalized.TrimEnd('/');

        return normalized;
    }
}
