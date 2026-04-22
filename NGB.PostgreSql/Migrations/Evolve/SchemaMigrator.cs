using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using NGB.Persistence.Migrations;
using NGB.Tools.Exceptions;
using Npgsql;

namespace NGB.PostgreSql.Migrations.Evolve;

/// <summary>
/// Discovers and runs schema migrations from one or more migration packs.
///
/// Design goals:
/// - Single Evolve run for multiple packs (platform + vertical modules).
/// - Explicit optional drift-repair per pack (kept separate from versioned migrations).
/// - Works well as a Kubernetes Job / CI deployment step.
/// </summary>
public static class SchemaMigrator
{
    public static IReadOnlyList<MigrationPack> DiscoverPacks(IEnumerable<Assembly> searchAssemblies)
    {
        if (searchAssemblies is null)
            throw new NgbArgumentInvalidException(nameof(searchAssemblies), "Search assemblies must be provided.");

        var packs = new List<MigrationPack>();

        foreach (var asm in searchAssemblies.Distinct())
        {
            foreach (var t in SafeGetTypes(asm))
            {
                if (t is null || t.IsAbstract || t.IsInterface)
                    continue;

                if (!typeof(IMigrationPackContributor).IsAssignableFrom(t))
                    continue;

                if (Activator.CreateInstance(t) is not IMigrationPackContributor contributor)
                    continue;

                packs.AddRange(contributor.GetPacks());
            }
        }

        // Validate uniqueness.
        var duplicates = packs
            .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .OrderBy(x => x)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new NgbInvariantViolationException(
                $"Duplicate migration pack ids: {string.Join(", ", duplicates)}",
                new Dictionary<string, object?> { ["duplicatePackIds"] = duplicates });
        }

        return packs;
    }

    /// <summary>
    /// Builds an ordered migration plan for the selected packs.
    ///
    /// This method does not touch the database.
    /// </summary>
    public static IReadOnlyList<MigrationPack> Plan(
        IReadOnlyList<MigrationPack> discoveredPacks,
        IReadOnlyCollection<string>? includePackIds = null)
    {
        if (discoveredPacks is null || discoveredPacks.Count == 0)
            throw new NgbArgumentInvalidException(nameof(discoveredPacks), "At least one migration pack must be discovered.");

        var selected = SelectWithDependencies(discoveredPacks, includePackIds);
        return TopologicallyOrder(selected);
    }

    /// <summary>
    /// Applies versioned migrations (Evolve) for the selected packs, optionally running drift-repair per pack.
    ///
    /// When <paramref name="dryRun"/> is true, no database operations are performed and <paramref name="connectionString"/> may be null.
    /// </summary>
    public static async Task<IReadOnlyList<MigrationPack>> MigrateAsync(
        string? connectionString,
        IReadOnlyList<MigrationPack> discoveredPacks,
        IReadOnlyCollection<string>? includePackIds = null,
        bool repair = false,
        bool dryRun = false,
        MigrationExecutionOptions? options = null,
        SchemaMigrationExecutionOptions? execOptions = null,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        if (!dryRun && string.IsNullOrWhiteSpace(connectionString))
            throw new NgbArgumentRequiredException(nameof(connectionString));

        if (discoveredPacks is null || discoveredPacks.Count == 0)
            throw new NgbArgumentInvalidException(nameof(discoveredPacks), "At least one migration pack must be discovered.");

        ct.ThrowIfCancellationRequested();

        var ordered = Plan(discoveredPacks, includePackIds);
        if (dryRun)
        {
            log?.Invoke("Dry run: database operations are skipped.");
            return ordered;
        }

        // Apply application_name (K8s-friendly observability), if requested.
        var csb = new NpgsqlConnectionStringBuilder(connectionString!);
        if (execOptions?.ApplicationName is { Length: > 0 } appName)
            csb.ApplicationName = appName;

        var effectiveCs = csb.ConnectionString;

        // Hold one global schema lock for the entire run (Evolve + optional repair).
        await using var guard = new NpgsqlConnection(effectiveCs);
        await guard.OpenAsync(ct);

        // Use UTC semantics for the whole migration session.
        await guard.ExecuteAsync(new CommandDefinition("SET TIME ZONE 'UTC';", cancellationToken: ct));

        // Apply timeouts early so lock waits and DDL are bounded.
        await ApplySessionOptionsAsync(guard, options, ct);

        var lockMode = execOptions?.LockMode ?? SchemaMigrationLockMode.Wait;
        var lockWait = execOptions?.LockWaitTimeout;

        var lockAcquired = await SchemaMigrationAdvisoryLock.AcquireOrSkipAsync(guard, lockMode, lockWait, log, ct);
        if (!lockAcquired)
            return []; // Skip mode: treat as no-op.

        try
        {
            // Run Evolve per pack with an isolated changelog table.
            //
            // Rationale: packs must have independent version streams. Otherwise a pack added later
            // (with versions lower than the current global lastAppliedVersion) would be silently skipped
            // when OutOfOrder=false (Evolve default).
            foreach (var pack in ordered)
            {
                var changelog = GetPackChangelogTableName(pack.Id);
                log?.Invoke($"Migrate: {pack.Id} (changelog={changelog})");

                await PostgresEvolveMigrator.MigrateAsync(
                    guard,
                    pack.MigrationAssemblies,
                    log: log,
                    metadataTableName: changelog,
                    metadataTableSchema: "public",
                    ct: ct);
            }

            if (repair)
            {
                // IMPORTANT: repair connections must NOT try to acquire the same advisory lock,
                // because we hold it in this session for the full duration.
                var repairOptions = options is null
                    ? new MigrationExecutionOptions(SkipAdvisoryLock: true)
                    : options with { SkipAdvisoryLock = true };

                foreach (var pack in ordered)
                {
                    var repairWithOptions = pack.RepairWithOptionsAsync;
                    var repairLegacy = pack.RepairAsync;

                    if (repairWithOptions is null && repairLegacy is null)
                        continue;

                    log?.Invoke($"Repair: {pack.Id}");

                    if (repairWithOptions is not null)
                        await repairWithOptions(effectiveCs, repairOptions, ct);
                    else if (repairLegacy is not null)
                        await repairLegacy(effectiveCs, ct);
                }
            }
        }
        finally
        {
            // Best-effort unlock.
            try
            {
                await SchemaMigrationAdvisoryLock.ReleaseAsync(guard, ct);
            }
            catch
            {
                // ignore
            }
        }

        return ordered;
    }

    private static string GetPackChangelogTableName(string packId)
    {
        // Keep pack changelogs isolated so packs can be installed later without forcing
        // a globally-monotonic version stream across all packs.
        var suffix = NormalizePackIdToIdentifier(packId);
        return $"migration_changelog__{suffix}";
    }

    private static string NormalizePackIdToIdentifier(string packId)
    {
        if (string.IsNullOrWhiteSpace(packId))
            return "pack";

        // Lowercase + replace non [a-z0-9_] with '_', collapse consecutive underscores.
        var sb = new StringBuilder(packId.Length);
        var prevUnderscore = false;

        foreach (var ch in packId.Trim())
        {
            var c = char.ToLowerInvariant(ch);
            var ok = c is >= 'a' and <= 'z' or >= '0' and <= '9';

            if (ok)
            {
                sb.Append(c);
                prevUnderscore = false;
            }
            else
            {
                if (!prevUnderscore)
                {
                    sb.Append('_');
                    prevUnderscore = true;
                }
            }
        }

        var s = sb.ToString().Trim('_');
        if (string.IsNullOrWhiteSpace(s))
            s = "pack";

        const int maxIdentifierLen = 63;
        const string prefix = "migration_changelog__";
        var maxSuffixLen = maxIdentifierLen - prefix.Length;
        if (maxSuffixLen < 8)
            return s;

        if (s.Length <= maxSuffixLen)
            return s;

        // Truncate and add a short stable hash suffix to avoid collisions.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        var hashSuffix = hex[..12];

        var keep = maxSuffixLen - 1 - hashSuffix.Length;
        if (keep < 1)
            keep = 1;

        return s[..keep] + "_" + hashSuffix;
    }

    private static async Task ApplySessionOptionsAsync(
        NpgsqlConnection connection,
        MigrationExecutionOptions? options,
        CancellationToken ct)
    {
        if (options is null)
            return;

        if (options.LockTimeout is not null)
        {
            var ms = (long)Math.Max(0, options.LockTimeout.Value.TotalMilliseconds);
            await connection.ExecuteAsync(new CommandDefinition($"SET lock_timeout = '{ms}ms';", cancellationToken: ct));
        }

        if (options.StatementTimeout is not null)
        {
            var ms = (long)Math.Max(0, options.StatementTimeout.Value.TotalMilliseconds);
            await connection.ExecuteAsync(new CommandDefinition($"SET statement_timeout = '{ms}ms';", cancellationToken: ct));
        }
    }

    private static IReadOnlyList<MigrationPack> SelectWithDependencies(
        IReadOnlyList<MigrationPack> all,
        IReadOnlyCollection<string>? includePackIds)
    {
        var byId = all.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

        HashSet<string> requested;
        if (includePackIds is null || includePackIds.Count == 0)
            requested = new HashSet<string>(byId.Keys, StringComparer.OrdinalIgnoreCase);
        else
            requested = new HashSet<string>(includePackIds.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);

        var selectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in requested)
        {
            AddWithDependencies(id);
        }

        return selectedIds.Select(id => byId[id]).ToArray();
        
        void AddWithDependencies(string id)
        {
            if (!byId.TryGetValue(id, out var pack))
                throw new NgbArgumentInvalidException(nameof(includePackIds), $"Unknown migration pack id: '{id}'.");

            if (!selectedIds.Add(pack.Id))
                return;

            if (pack.DependsOn is null)
                return;

            foreach (var depId in pack.DependsOn)
            {
                if (string.IsNullOrWhiteSpace(depId))
                    continue;

                AddWithDependencies(depId);
            }
        }
    }

    private static IReadOnlyList<MigrationPack> TopologicallyOrder(IReadOnlyList<MigrationPack> selected)
    {
        var byId = selected.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

        // Build graph: dep -> dependent.
        var outgoing = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in selected)
        {
            outgoing[p.Id] = new List<string>();
            inDegree[p.Id] = 0;
        }

        foreach (var p in selected)
        {
            var deps = p.DependsOn ?? Array.Empty<string>();
            foreach (var dep in deps)
            {
                if (string.IsNullOrWhiteSpace(dep))
                    continue;

                if (!byId.ContainsKey(dep))
                {
                    // Should not happen after SelectWithDependencies.
                    throw new NgbInvariantViolationException(
                        $"Missing dependency pack '{dep}' for '{p.Id}'.",
                        new Dictionary<string, object?> { ["packId"] = p.Id, ["missingDependency"] = dep });
                }

                outgoing[dep].Add(p.Id);
                inDegree[p.Id]++;
            }
        }

        var queue = new Queue<string>(inDegree.Where(kvp => kvp.Value == 0).Select(kvp => kvp.Key).OrderBy(x => x));
        var orderedIds = new List<string>(selected.Count);

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            orderedIds.Add(id);

            foreach (var to in outgoing[id])
            {
                inDegree[to]--;
                if (inDegree[to] == 0)
                    queue.Enqueue(to);
            }
        }

        if (orderedIds.Count != selected.Count)
        {
            var cycle = inDegree.Where(kvp => kvp.Value > 0).Select(kvp => kvp.Key).OrderBy(x => x).ToArray();
            throw new NgbInvariantViolationException(
                "Migration pack dependency cycle detected.",
                new Dictionary<string, object?> { ["cyclePackIds"] = cycle });
        }

        return orderedIds.Select(id => byId[id]).ToArray();
    }

    private static IEnumerable<Type?> SafeGetTypes(Assembly asm)
    {
        try
        {
            return asm.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types;
        }
        catch
        {
            return [];
        }
    }
}
