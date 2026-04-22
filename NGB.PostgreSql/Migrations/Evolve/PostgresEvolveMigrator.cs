using System.Reflection;
using Npgsql;
using NGB.Persistence.Migrations;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Migrations.Evolve;

/// <summary>
/// Schema versioning migrator powered by Evolve.
/// </summary>
public static class PostgresEvolveMigrator
{
    public static Task MigrateAsync(
        string connectionString,
        IReadOnlyCollection<Assembly> migrationAssemblies,
        MigrationExecutionOptions? options = null,
        Action<string>? log = null,
        string? metadataTableName = null,
        string? metadataTableSchema = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new NgbArgumentRequiredException(nameof(connectionString));

        if (migrationAssemblies is null || migrationAssemblies.Count == 0)
            throw new NgbArgumentInvalidException(nameof(migrationAssemblies), "At least one migration assembly must be provided.");

        ct.ThrowIfCancellationRequested();

        using var conn = new NpgsqlConnection(connectionString);
        conn.Open();

        ApplySessionDefaults(conn, options);

        return MigrateAsync(conn, migrationAssemblies, log, metadataTableName, metadataTableSchema, ct);
    }

    /// <summary>
    /// Runs Evolve migrations using an existing open connection.
    /// </summary>
    public static Task MigrateAsync(
        NpgsqlConnection connection,
        IReadOnlyCollection<Assembly> migrationAssemblies,
        Action<string>? log = null,
        string? metadataTableName = null,
        string? metadataTableSchema = null,
        CancellationToken ct = default)
    {
        if (connection is null)
            throw new NgbArgumentRequiredException(nameof(connection));

        if (migrationAssemblies is null || migrationAssemblies.Count == 0)
            throw new NgbArgumentInvalidException(nameof(migrationAssemblies), "At least one migration assembly must be provided.");

        ct.ThrowIfCancellationRequested();

        var embeddedFilters = BuildEmbeddedResourceFilters(migrationAssemblies);
        EnsureEmbeddedMigrationsDiscovered(migrationAssemblies, embeddedFilters);

        // Evolve is synchronous. Keep this wrapper deterministic and dependency-free.
        // NOTE: Evolve is in namespace EvolveDb, but this file lives under our own *.Migrations.Evolve namespace.
        // Use global:: to avoid ambiguous reference resolution.
        var evolve = new global::EvolveDb.Evolve(connection, message => log?.Invoke(message))
        {
            // Never allow schema erase from application code.
            IsEraseDisabled = true,

            // We use our own global advisory lock to guarantee single-writer semantics.
            EnableClusterMode = false,

            // Changelog contract.
            MetadataTableSchema = string.IsNullOrWhiteSpace(metadataTableSchema) ? "public" : metadataTableSchema,
            MetadataTableName = string.IsNullOrWhiteSpace(metadataTableName) ? "migration_changelog" : metadataTableName,

            Schemas = ["public"],

            // Embedded migration scripts.
            EmbeddedResourceAssemblies = migrationAssemblies.ToArray(),

            // IMPORTANT:
            // We only consider scripts placed directly under "db/migrations" and named using Evolve conventions:
            // - versioned:  V...sql
            // - repeatable: R__...sql
            // This keeps the contract deterministic and prevents accidental discovery of scripts from nested folders
            // (which can cause "out-of-order pending migration" validation errors when such a file is added later).
            EmbeddedResourceFilters = embeddedFilters,
        };

        evolve.Migrate();

        return Task.CompletedTask;
    }

    private static string[] BuildEmbeddedResourceFilters(IReadOnlyCollection<Assembly> migrationAssemblies)
    {
        // Evolve embedded resource loader uses StartsWith(filter, OrdinalIgnoreCase).
        // Embedded resource names use the default namespace prefix, which typically matches assembly name.
        // Example: <Asm>.db.migrations.V2026_02_20_0001__baseline.sql
        return migrationAssemblies
            .SelectMany(a =>
            {
                var asmName = a.GetName().Name;
                return new[]
                {
                    $"{asmName}.db.migrations.V",
                    $"{asmName}.db.migrations.R",
                };
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void EnsureEmbeddedMigrationsDiscovered(
        IReadOnlyCollection<Assembly> migrationAssemblies,
        IReadOnlyList<string> embeddedFilters)
    {
        // Fail fast if the host is misconfigured (wrong assembly, scripts not embedded, wrong folder).
        // Otherwise we'll get late runtime failures like "relation does not exist".
        foreach (var asm in migrationAssemblies)
        {
            var anyMatch = false;
            foreach (var res in SafeGetManifestResourceNames(asm))
            {
                if (!res.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (embeddedFilters.Any(f => res.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
                {
                    anyMatch = true;
                    break;
                }
            }

            if (!anyMatch)
            {
                var asmName = asm.GetName().Name ?? "<unknown>";
                var sample = SafeGetManifestResourceNames(asm)
                    .Where(x => x.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .Take(30)
                    .ToArray();

                throw new NgbInvariantViolationException(
                    $"No embedded migration scripts were discovered for assembly '{asmName}'. " +
                    "Expected embedded SQL resources under db/migrations (V...sql and/or R__...sql). " +
                    "Check <EmbeddedResource Include=\"db/migrations/**/*.sql\"/> and resource naming.",
                    new Dictionary<string, object?>
                    {
                        ["assembly"] = asmName,
                        ["embeddedFilters"] = embeddedFilters.ToArray(),
                        ["sqlResourceSample"] = sample,
                    });
            }
        }
    }

    private static IReadOnlyList<string> SafeGetManifestResourceNames(Assembly asm)
    {
        try
        {
            return asm.GetManifestResourceNames();
        }
        catch
        {
            return [];
        }
    }

    private static void ApplySessionDefaults(NpgsqlConnection connection, MigrationExecutionOptions? options)
    {
        // Deterministic UTC semantics.
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SET TIME ZONE 'UTC';";
            cmd.ExecuteNonQuery();
        }

        if (options is null)
            return;

        if (options.LockTimeout is not null)
        {
            var ms = (long)Math.Max(0, options.LockTimeout.Value.TotalMilliseconds);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SET lock_timeout = '{ms}ms';";
            cmd.ExecuteNonQuery();
        }

        if (options.StatementTimeout is not null)
        {
            var ms = (long)Math.Max(0, options.StatementTimeout.Value.TotalMilliseconds);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SET statement_timeout = '{ms}ms';";
            cmd.ExecuteNonQuery();
        }
    }
}
