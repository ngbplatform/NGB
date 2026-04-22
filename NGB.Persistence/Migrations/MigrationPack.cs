using System.Reflection;

namespace NGB.Persistence.Migrations;

/// <summary>
/// A logical migration pack (platform module / vertical solution module).
///
/// Packs are discovered by <see cref="IMigrationPackContributor"/> implementations.
/// A migrator may apply multiple packs in a single Evolve run by combining their assemblies.
/// </summary>
public sealed record MigrationPack(
    string Id,
    IReadOnlyCollection<Assembly> MigrationAssemblies,
    IReadOnlyCollection<string>? DependsOn = null,
    Func<string, CancellationToken, Task>? RepairAsync = null,
    Func<string, MigrationExecutionOptions?, CancellationToken, Task>? RepairWithOptionsAsync = null);
