using NGB.Tools.Exceptions;

namespace NGB.BackgroundJobs.Hosting;

public sealed class BackgroundJobsHostingBootstrap(
    BackgroundJobsHostingOptions options,
    string applicationConnectionString,
    string hangfireConnectionString)
{
    public BackgroundJobsHostingOptions Options { get; } = options ?? throw new NgbArgumentRequiredException(nameof(options));

    public string ApplicationConnectionString { get; } = string.IsNullOrWhiteSpace(applicationConnectionString)
        ? throw new NgbArgumentRequiredException(nameof(applicationConnectionString))
        : applicationConnectionString.Trim();

    public string HangfireConnectionString { get; } = string.IsNullOrWhiteSpace(hangfireConnectionString)
        ? throw new NgbArgumentRequiredException(nameof(hangfireConnectionString))
        : hangfireConnectionString.Trim();

    public Task EnsureInfrastructureAsync()
        => Infrastructure.HangfireTools.EnsureDatabaseExistsAsync(HangfireConnectionString);
}
