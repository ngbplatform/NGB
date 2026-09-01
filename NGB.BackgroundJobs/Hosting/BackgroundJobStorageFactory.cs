using Hangfire;

namespace NGB.BackgroundJobs.Hosting;

/// <summary>
/// Composition-root boundary for provider-specific Hangfire storage creation.
/// </summary>
public delegate JobStorage BackgroundJobStorageFactory(
    string connectionString,
    string storageNamespace,
    bool prepareSchemaIfNecessary);
