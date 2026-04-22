using NGB.Persistence.Migrations;
using NGB.PostgreSql.Migrations.Evolve;

namespace NGB.Migrator.Core;

/// <summary>
/// Shared command-line migrator runner used by application-specific migrator hosts
/// (e.g. NGB.Demo.Trade.Migrator).
/// </summary>
public static class PlatformMigratorCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        var k8s = HasFlag(args, "--k8s")
                  || string.Equals(Environment.GetEnvironmentVariable("NGB_K8S_MODE"), "true", StringComparison.OrdinalIgnoreCase);

        var repair = HasFlag(args, "--repair");
        var dryRun = HasFlag(args, "--dry-run") || HasFlag(args, "--dryrun") || HasFlag(args, "--plan-only");
        var listModules = HasFlag(args, "--list-modules");
        var info = HasFlag(args, "--info");
        var showScripts = HasFlag(args, "--show-scripts");

        var modules = ParseModules(args);

        // Connection string is required only when we are going to touch the database.
        var connectionString = GetArgValue(args, "--connection") ??
                               Environment.GetEnvironmentVariable("NGB_CONNECTION_STRING");

        var needsConnection = !(listModules || dryRun);
        if (needsConnection && string.IsNullOrWhiteSpace(connectionString))
        {
            await Console.Error.WriteLineAsync("Missing connection string. Provide --connection=\"...\" or set NGB_CONNECTION_STRING.");
            return MigratorExitCodes.InvalidArguments;
        }

        var options = BuildExecutionOptions(args);
        var execOptions = BuildSchemaExecutionOptions(args, k8s);

        try
        {
            // Discover packs from the entry assembly + its referenced assemblies.
            // NOTE: In .NET, referenced assemblies are not always loaded until a type is used.
            // We proactively load references to make pack discovery deterministic.
            var searchAssemblies = MigrationAssemblyDiscovery.LoadForPackDiscovery();
            var packs = SchemaMigrator.DiscoverPacks(searchAssemblies);

            if (listModules)
            {
                Console.WriteLine("Discovered migration packs:");

                foreach (var p in packs.OrderBy(x => x.Id))
                {
                    var asmNames = p.MigrationAssemblies
                        .Select(a => a.GetName().Name)
                        .Distinct()
                        .OrderBy(x => x);

                    Console.WriteLine($"- {p.Id}  [{string.Join(", ", asmNames)}]");
                }

                return MigratorExitCodes.Success;
            }

            if (info || dryRun)
            {
                var plan = SchemaMigrator.Plan(packs, modules);

                Console.WriteLine("Migration plan:");

                foreach (var p in plan)
                {
                    var deps = p.DependsOn is null || p.DependsOn.Count == 0 ? "-" : string.Join(", ", p.DependsOn);
                    var asmNames = p.MigrationAssemblies
                        .Select(a => a.GetName().Name)
                        .Distinct()
                        .OrderBy(x => x);

                    Console.WriteLine($"- {p.Id}  deps=[{deps}]  assemblies=[{string.Join(", ", asmNames)}]");
                }

                Console.WriteLine($"Repair: {repair}");
                Console.WriteLine($"DryRun: {dryRun}");
                Console.WriteLine($"K8sMode: {k8s}");

                if (!string.IsNullOrWhiteSpace(execOptions.ApplicationName))
                    Console.WriteLine($"ApplicationName: {execOptions.ApplicationName}");

                Console.WriteLine($"SchemaLockMode: {execOptions.LockMode}");
                Console.WriteLine($"SchemaLockWaitSeconds: {execOptions.LockWaitTimeout?.TotalSeconds}");

                var assemblies = plan.SelectMany(p => p.MigrationAssemblies).Distinct().ToArray();
                PrintEmbeddedScripts(assemblies, showScripts);
            }

            if (dryRun)
            {
                Console.WriteLine("OK: dry run completed (no DB operations).");
                return MigratorExitCodes.Success;
            }

            var applied = await SchemaMigrator.MigrateAsync(
                connectionString!,
                packs,
                includePackIds: modules,
                repair: repair,
                dryRun: false,
                options: options,
                execOptions: execOptions,
                log: Console.WriteLine);

            if (applied.Count == 0)
            {
                Console.WriteLine("OK: skipped (schema lock held by another migrator).");
                return MigratorExitCodes.Success;
            }

            Console.WriteLine($"OK: migrated packs: {string.Join(", ", applied.Select(p => p.Id))}.");
            return MigratorExitCodes.Success;
        }
        catch (SchemaMigrationLockNotAcquiredException ex)
        {
            await Console.Error.WriteLineAsync("LOCKED: schema migration lock not acquired.");
            Console.Error.WriteLine(ex);
            return MigratorExitCodes.LockNotAcquired;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync("FAILED: database migration error.");
            Console.Error.WriteLine(ex);
            return MigratorExitCodes.Failure;
        }
    }

    private static bool HasFlag(string[] args, string name)
        => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

    private static string? GetArgValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
                return i + 1 < args.Length ? args[i + 1] : null;

            var prefix = name + "=";
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return arg[prefix.Length..];
        }

        return null;
    }

    private static IReadOnlyCollection<string>? ParseModules(string[] args)
    {
        // --modules platform,demo.trade
        var raw = GetArgValue(args, "--modules");
        if (!string.IsNullOrWhiteSpace(raw))
        {
            return raw
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
        }

        // repeated --module foo --module bar
        var list = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--module", StringComparison.OrdinalIgnoreCase))
                continue;

            if (i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1]))
                list.Add(args[i + 1]);
        }

        return list.Count == 0 ? null : list;
    }

    private static MigrationExecutionOptions? BuildExecutionOptions(string[] args)
    {
        static int? ReadInt(string[] args, string name)
        {
            var raw = GetArgValue(args, name);
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            return int.TryParse(raw, out var v) ? v : null;
        }

        // seconds, optional
        var lockSeconds = ReadInt(args, "--lock-timeout-seconds") ?? ReadInt(args, "--lock-timeout");
        var stmtSeconds = ReadInt(args, "--statement-timeout-seconds") ?? ReadInt(args, "--statement-timeout");

        TimeSpan? lockTimeout = lockSeconds is > 0 ? TimeSpan.FromSeconds(lockSeconds.Value) : null;
        TimeSpan? stmtTimeout = stmtSeconds is > 0 ? TimeSpan.FromSeconds(stmtSeconds.Value) : null;

        if (lockTimeout is null && stmtTimeout is null)
            return null;

        return new MigrationExecutionOptions(lockTimeout, stmtTimeout);
    }

    private static SchemaMigrationExecutionOptions BuildSchemaExecutionOptions(string[] args, bool k8s)
    {
        var appName =
            GetArgValue(args, "--application-name") ??
            GetArgValue(args, "--app-name") ??
            Environment.GetEnvironmentVariable("NGB_APPLICATION_NAME");

        if (string.IsNullOrWhiteSpace(appName) && k8s)
        {
            var pod = Environment.GetEnvironmentVariable("HOSTNAME");
            appName = string.IsNullOrWhiteSpace(pod)
                ? "ngb-migrator"
                : $"ngb-migrator:{TrimMax(pod, 32)}";
        }

        var modeRaw = GetArgValue(args, "--schema-lock-mode") ??
                      Environment.GetEnvironmentVariable("NGB_SCHEMA_LOCK_MODE");

        var lockMode = ParseLockMode(modeRaw, defaultMode: SchemaMigrationLockMode.Wait);

        var waitSecondsRaw =
            GetArgValue(args, "--schema-lock-wait-seconds") ??
            GetArgValue(args, "--schema-lock-wait") ??
            Environment.GetEnvironmentVariable("NGB_SCHEMA_LOCK_WAIT_SECONDS");

        TimeSpan? waitTimeout = null;
        if (int.TryParse(waitSecondsRaw, out var ws) && ws > 0)
        {
            waitTimeout = TimeSpan.FromSeconds(ws);
        }
        else if (k8s)
        {
            // Default: avoid overlapping CronJobs/Jobs.
            waitTimeout = TimeSpan.FromMinutes(30);
        }

        return new SchemaMigrationExecutionOptions(appName, lockMode, waitTimeout);
    }

    private static SchemaMigrationLockMode ParseLockMode(string? raw, SchemaMigrationLockMode defaultMode)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return defaultMode;

        return raw.Trim().ToLowerInvariant() switch
        {
            "wait" => SchemaMigrationLockMode.Wait,
            "try" => SchemaMigrationLockMode.Try,
            "skip" => SchemaMigrationLockMode.Skip,
            _ => defaultMode,
        };
    }

    private static string TrimMax(string s, int max)
        => s.Length <= max ? s : s.Substring(0, max);

    private static void PrintEmbeddedScripts(IReadOnlyCollection<System.Reflection.Assembly> assemblies, bool showScripts)
    {
        var all = new List<string>();

        foreach (var asm in assemblies)
        {
            var prefix = asm.GetName().Name + ".db.migrations.";
            var names = asm
                .GetManifestResourceNames()
                .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (names.Length == 0)
                continue;

            all.AddRange(names);
        }

        if (all.Count == 0)
        {
            Console.WriteLine("Embedded scripts: <none>");
            return;
        }

        var versioned = all.Count(n => n.Contains(".db.migrations.V", StringComparison.OrdinalIgnoreCase));
        var repeatable = all.Count(n => n.Contains(".db.migrations.R__", StringComparison.OrdinalIgnoreCase));

        Console.WriteLine($"Embedded scripts: total={all.Count}, versioned={versioned}, repeatable={repeatable}");

        if (showScripts)
        {
            foreach (var n in all)
            {
                Console.WriteLine($"  - {n}");
            }
        }
        else
        {
            Console.WriteLine("Use --show-scripts to print all embedded script resource names.");
        }
    }
}
