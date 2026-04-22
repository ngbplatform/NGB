using NGB.Tools.Exceptions;

namespace NGB.BackgroundJobs.Hosting;

public sealed class BackgroundJobsHostingOptions
{
    public string HealthPath { get; set; } = "/health";

    public string DashboardPath { get; set; } = "/hangfire";

    public string DashboardTitle { get; set; } = "NGB: Background Jobs";

    public string DashboardBrandSubtitle { get; set; } = "Background Jobs";

    public string? AdminConsoleCallbackPath { get; set; }

    public string? AdminConsolePublicOrigin { get; set; }

    public string BackgroundJobsSectionName { get; set; } = "BackgroundJobs";

    public string ApplicationConnectionStringName { get; set; } = "DefaultConnection";

    public string HangfireConnectionStringName { get; set; } = "Hangfire";

    public string PostgresHealthCheckName { get; set; } = "PostgreSQL Server";

    public string HangfireHealthCheckName { get; set; } = "Jobs";

    public int HangfireHealthCheckMaximumFailedJobs { get; set; } = 1;

    public bool PrepareHangfireSchemaIfNecessary { get; set; } = true;

    public int WorkerCount { get; set; } = Math.Max(1, Environment.ProcessorCount);

    public int DistributedLockTimeoutSeconds { get; set; } = 1;

    public string? ServerName { get; set; }

    public bool RequireDashboardAuthorization { get; set; } = true;

    public bool MapAccountEndpoints { get; set; } = true;

    public IList<string> DashboardStylesheetPaths { get; } = ["hangfire-dashboard.css"];

    public IList<string> Queues { get; } = ["default"];

    public BackgroundJobsHostingOptions AddCustomStylesheet(string stylesheetPath)
    {
        if (string.IsNullOrWhiteSpace(stylesheetPath))
            throw new NgbArgumentRequiredException(nameof(stylesheetPath));

        DashboardStylesheetPaths.Add(stylesheetPath.Trim());
        return this;
    }

    internal void ValidateAndNormalize()
    {
        HealthPath = NormalizePath(HealthPath, nameof(HealthPath));
        DashboardPath = NormalizePath(DashboardPath, nameof(DashboardPath));

        DashboardTitle = NormalizeRequiredText(DashboardTitle, nameof(DashboardTitle));
        DashboardBrandSubtitle = NormalizeRequiredText(DashboardBrandSubtitle, nameof(DashboardBrandSubtitle));
        BackgroundJobsSectionName = NormalizeRequiredText(BackgroundJobsSectionName, nameof(BackgroundJobsSectionName));
        ApplicationConnectionStringName = NormalizeRequiredText(ApplicationConnectionStringName, nameof(ApplicationConnectionStringName));
        HangfireConnectionStringName = NormalizeRequiredText(HangfireConnectionStringName, nameof(HangfireConnectionStringName));
        PostgresHealthCheckName = NormalizeRequiredText(PostgresHealthCheckName, nameof(PostgresHealthCheckName));
        HangfireHealthCheckName = NormalizeRequiredText(HangfireHealthCheckName, nameof(HangfireHealthCheckName));

        if (WorkerCount <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(WorkerCount), WorkerCount, "WorkerCount must be positive.");

        if (DistributedLockTimeoutSeconds <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(DistributedLockTimeoutSeconds), DistributedLockTimeoutSeconds, "DistributedLockTimeoutSeconds must be positive.");

        for (var i = Queues.Count - 1; i >= 0; i--)
        {
            if (string.IsNullOrWhiteSpace(Queues[i]))
                Queues.RemoveAt(i);
            else
                Queues[i] = Queues[i].Trim();
        }

        if (Queues.Count == 0)
            Queues.Add("default");

        for (var i = DashboardStylesheetPaths.Count - 1; i >= 0; i--)
        {
            if (string.IsNullOrWhiteSpace(DashboardStylesheetPaths[i]))
                DashboardStylesheetPaths.RemoveAt(i);
            else
                DashboardStylesheetPaths[i] = DashboardStylesheetPaths[i].Trim();
        }

        AdminConsoleCallbackPath = string.IsNullOrWhiteSpace(AdminConsoleCallbackPath)
            ? null
            : NormalizePath(AdminConsoleCallbackPath, nameof(AdminConsoleCallbackPath));

        AdminConsolePublicOrigin = string.IsNullOrWhiteSpace(AdminConsolePublicOrigin)
            ? null
            : NormalizePublicOrigin(AdminConsolePublicOrigin);

        ServerName = string.IsNullOrWhiteSpace(ServerName)
            ? null
            : ServerName.Trim();
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

    private static string NormalizeRequiredText(string value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new NgbArgumentRequiredException(propertyName);

        return value.Trim();
    }

    private static string NormalizePublicOrigin(string publicOrigin)
    {
        var normalized = publicOrigin.Trim().TrimEnd('/');
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new NgbConfigurationViolationException(
                "AdminConsolePublicOrigin must be an absolute http/https URL.",
                new Dictionary<string, object?>
                {
                    ["publicOrigin"] = publicOrigin
                });
        }

        return normalized;
    }
}
