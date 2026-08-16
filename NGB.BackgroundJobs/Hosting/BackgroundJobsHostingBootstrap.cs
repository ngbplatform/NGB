using NGB.Tools.Exceptions;

namespace NGB.BackgroundJobs.Hosting;

public sealed class BackgroundJobsHostingBootstrap
{
    private readonly Func<string, Task> _ensureDatabaseExistsAsync;

    public BackgroundJobsHostingBootstrap(
        BackgroundJobsHostingOptions options,
        string applicationConnectionString,
        string hangfireConnectionString)
        : this(options, applicationConnectionString, hangfireConnectionString, Infrastructure.HangfireTools.EnsureDatabaseExistsAsync)
    {
    }

    internal BackgroundJobsHostingBootstrap(
        BackgroundJobsHostingOptions options,
        string applicationConnectionString,
        string hangfireConnectionString,
        Func<string, Task> ensureDatabaseExistsAsync)
    {
        Options = options ?? throw new NgbArgumentRequiredException(nameof(options));

        ApplicationConnectionString = string.IsNullOrWhiteSpace(applicationConnectionString)
            ? throw new NgbArgumentRequiredException(nameof(applicationConnectionString))
            : applicationConnectionString.Trim();

        HangfireConnectionString = string.IsNullOrWhiteSpace(hangfireConnectionString)
            ? throw new NgbArgumentRequiredException(nameof(hangfireConnectionString))
            : hangfireConnectionString.Trim();

        _ensureDatabaseExistsAsync = ensureDatabaseExistsAsync
            ?? throw new NgbArgumentRequiredException(nameof(ensureDatabaseExistsAsync));
    }

    public BackgroundJobsHostingOptions Options { get; }

    public string ApplicationConnectionString { get; }

    public string HangfireConnectionString { get; }

    public Task EnsureInfrastructureAsync() => _ensureDatabaseExistsAsync(HangfireConnectionString);
}
