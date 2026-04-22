using NGB.PostgreSql.Migrations.Evolve;
using NGB.Persistence.Migrations;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Bootstrap;

/// <summary>
/// Small helper for host applications that want an explicit opt-in schema init.
///
/// Production guidance:
/// - Prefer running schema migrations via an application migrator (CI step / Kubernetes Job).
/// - Keep <see cref="SchemaInitMode.None"/> in production appsettings.
/// </summary>
public static class SchemaInitRunner
{
    public static async Task RunAsync(
        string connectionString,
        SchemaInitMode mode,
        IReadOnlyCollection<string>? includePackIds = null,
        bool dryRun = false,
        MigrationExecutionOptions? options = null,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new NgbArgumentRequiredException(nameof(connectionString));

        ct.ThrowIfCancellationRequested();

        if (mode == SchemaInitMode.None)
        {
            log?.Invoke("Schema init: disabled (mode=None).");
            return;
        }

        var discoveryAssemblies = MigrationAssemblyDiscovery.LoadForPackDiscovery();
        var packs = SchemaMigrator.DiscoverPacks(discoveryAssemblies);
        var plan = SchemaMigrator.Plan(packs, includePackIds);

        log?.Invoke("Schema init plan:");
        foreach (var p in plan)
        {
            var deps = p.DependsOn is null || p.DependsOn.Count == 0 ? "-" : string.Join(", ", p.DependsOn);
            log?.Invoke($"- {p.Id}  deps=[{deps}]");
        }

        switch (mode)
        {
            case SchemaInitMode.Migrate:
                await SchemaMigrator.MigrateAsync(connectionString, packs, includePackIds, repair: false, dryRun: dryRun, options: options, log: log, ct: ct);
                return;

            case SchemaInitMode.MigrateAndRepair:
                await SchemaMigrator.MigrateAsync(connectionString, packs, includePackIds, repair: true, dryRun: dryRun, options: options, log: log, ct: ct);
                return;

            case SchemaInitMode.Repair:
                foreach (var p in plan)
                {
                    var repairWithOptions = p.RepairWithOptionsAsync;
                    var repairLegacy = p.RepairAsync;

                    if (repairWithOptions is null && repairLegacy is null)
                        continue;

                    log?.Invoke($"Repair: {p.Id}");

                    if (dryRun)
                        continue;

                    if (repairWithOptions is not null)
                        await repairWithOptions(connectionString, options, ct);
                    else if (repairLegacy is not null)
                        await repairLegacy(connectionString, ct);
                }

                return;

            default:
                throw new NgbArgumentInvalidException(nameof(mode), $"Unsupported schema init mode: {mode}.");
        }
    }
}
