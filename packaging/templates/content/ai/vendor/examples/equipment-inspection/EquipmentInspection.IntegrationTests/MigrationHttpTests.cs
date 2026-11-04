using System.Diagnostics;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using EquipmentInspection.Definitions;
using EquipmentInspection.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Persistence.Migrations;
using NGB.PostgreSql.DependencyInjection;
using NGB.PostgreSql.Migrations.Evolve;
using NGB.Runtime.DependencyInjection;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class MigrationHttpTests
{
    [Fact]
    public async Task Frozen_application_schema_upgrades_repeatably_and_preserves_existing_records_and_audit()
    {
        DocumentDto? original = null;
        await using var host = new InspectionHost(prepareDatabase: async connection =>
        {
            var baseline = Assembly.Load("EquipmentInspection.MigrationBaseline");
            await VerifyBaselineAsync(baseline);
            var packs = Packs(baseline);
            await SchemaMigrator.MigrateAsync(connection, packs);
            await SchemaMigrator.MigrateAsync(connection, packs);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddNgbRuntime().AddNgbPostgres(connection);
            MigrationBaseline.Configure(services);
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var equipment = await catalogs.CreateAsync(MigrationBaseline.EquipmentCode,
                Payload(("display", "Equipment before schema upgrade")), CancellationToken.None);
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
            original = await documents.CreateDraftAsync(MigrationBaseline.InspectionCode,
                Payload(("display", "Inspection before schema upgrade"), ("equipment_id", equipment.Id), ("result", "Passed")),
                CancellationToken.None);
        });
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var path = "/api/documents/" + EquipmentDefinitions.InspectionCode + "/" + original!.Id;
        var after = (await admin.GetFromJsonAsync<DocumentDto>(path, Json))!;
        AssertPreserved(original, after);
        var auditPath = $"/api/audit/entities/Document/{original.Id}";
        var audit = await admin.GetStringAsync(auditPath);
        Assert.Contains("document.create_draft", audit);
        var checklist = await PostAsync<DocumentDto>(admin, "/api/documents/" + ChecklistDefinitions.DocumentCode, new
        {
            fields = new { display = "New schema checklist", reviewed = true },
            parts = new
            {
                checks = new
                {
                    rows = new[] { new { line_no = 1, description = "Guard", passed = true } }
                }
            }
        });
        Assert.Single(checklist.Payload.Parts![ChecklistDefinitions.ChecksPartCode].Rows);
        var rerun = await RunMigratorAsync("--connection=" + host.ConnectionString, "--modules=equipment-inspection");
        Assert.True(rerun.ExitCode == 0, rerun.Error.Replace(host.ConnectionString, "[isolated database]", StringComparison.Ordinal));
        Assert.Equal(JsonSerializer.Serialize(after, Json), JsonSerializer.Serialize(await admin.GetFromJsonAsync<DocumentDto>(path, Json), Json));
        Assert.Equal(audit, await admin.GetStringAsync(auditPath));
        Assert.Single((await admin.GetFromJsonAsync<DocumentDto>(
            "/api/documents/" + ChecklistDefinitions.DocumentCode + "/" + checklist.Id, Json))!.Payload.Parts![ChecklistDefinitions.ChecksPartCode].Rows);

        var drift = Assembly.Load("EquipmentInspection.MigrationChecksumDrift");
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => SchemaMigrator.MigrateAsync(host.ConnectionString, Packs(drift)));
        Assert.Contains("checksum", failure.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(audit, await admin.GetStringAsync(auditPath));
        Assert.Equal(JsonSerializer.Serialize(after, Json), JsonSerializer.Serialize(await admin.GetFromJsonAsync<DocumentDto>(path, Json), Json));
        var recovery = await RunMigratorAsync("--connection=" + host.ConnectionString);
        Assert.True(recovery.ExitCode == 0, recovery.Error.Replace(host.ConnectionString, "[isolated database]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Failed_migration_stops_the_test_startup_sequence_before_API_composition()
    {
        var apiComposed = false;
        await using var host = new InspectionHost(
            configureServices: _ => apiComposed = true,
            prepareDatabase: async connection =>
            {
                await SchemaMigrator.MigrateAsync(connection, Packs(Assembly.Load("EquipmentInspection.MigrationBaseline")));
                await SchemaMigrator.MigrateAsync(connection, Packs(Assembly.Load("EquipmentInspection.MigrationChecksumDrift")));
            });
        var failure = await Assert.ThrowsAnyAsync<Exception>(host.StartAsync);
        Assert.Contains("checksum", failure.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(apiComposed);
    }

    [Fact]
    public async Task Real_migrator_process_requires_storage_and_dry_run_resolves_pack_dependencies_without_connecting()
    {
        var missing = await RunMigratorAsync();
        Assert.Equal(2, missing.ExitCode);
        Assert.Contains("Missing connection string", missing.Error);
        var list = await RunMigratorAsync("--list-modules");
        Assert.Equal(0, list.ExitCode);
        Assert.Contains("equipment-inspection", list.Output);
        Assert.Contains("platform", list.Output);
        Assert.DoesNotContain("MigrationBaseline", list.Output);
        Assert.DoesNotContain("MigrationChecksumDrift", list.Output);
        var dryRun = await RunMigratorAsync("--dry-run", "--show-scripts", "--modules=equipment-inspection",
            "--connection=not-a-connection");
        Assert.Equal(0, dryRun.ExitCode);
        Assert.Contains("no DB operations", dryRun.Output);
        Assert.True(dryRun.Output.IndexOf("- platform", StringComparison.Ordinal)
            < dryRun.Output.IndexOf("- equipment-inspection", StringComparison.Ordinal));
        Assert.Contains("inspection_checklist.sql", dryRun.Output);
        var unknown = await RunMigratorAsync("--dry-run", "--modules=missing-pack");
        Assert.Equal(1, unknown.ExitCode);
        Assert.Contains("missing-pack", unknown.Error);
    }

    private static IReadOnlyList<MigrationPack> Packs(Assembly application)
    {
        return new PlatformMigrationPackContributor().GetPacks()
            .Append(new MigrationPack("equipment-inspection", [application], ["platform"]))
            .ToArray();
    }

    private static void AssertPreserved(DocumentDto before, DocumentDto after)
    {
        // Added fields have their own migration assertions; every previously stored field must survive.
        var fields = before.Payload.Fields!.Keys.ToDictionary(key => key, key => after.Payload.Fields![key]);
        var preserved = after with { Payload = after.Payload with { Fields = fields } };
        Assert.True(JsonElement.DeepEquals(
            JsonSerializer.SerializeToElement(before, Json),
            JsonSerializer.SerializeToElement(preserved, Json)));
    }

    private static async Task VerifyBaselineAsync(Assembly baseline)
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "migration-baseline.json")));
        var files = manifest.RootElement.GetProperty("files");
        Assert.Equal(4, files.EnumerateObject().Count());
        foreach (var file in files.EnumerateObject())
        {
            await using var stream = baseline.GetManifestResourceStream(baseline.GetName().Name + ".db.migrations." + file.Name)!;
            Assert.NotNull(stream);
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
            Assert.Equal(file.Value.GetString(), hash);
        }
    }

    private static async Task<ProcessResult> RunMigratorAsync(params string[] args)
    {
        // Run the host's own output: the test host may omit libraries supplied by its ASP.NET shared framework.
        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configurationDirectory = frameworkDirectory.Parent!;
        var solution = configurationDirectory.Parent!.Parent!.Parent!;
        var hostDirectory = Path.Combine(solution.FullName, "EquipmentInspection.Migrator", "bin",
            configurationDirectory.Name, frameworkDirectory.Name);
        var hostAssembly = Path.Combine(hostDirectory, Path.GetFileName(typeof(Migrator.Program).Assembly.Location));
        Assert.True(File.Exists(hostAssembly), "Build the Migrator project before running its process acceptance.");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = hostDirectory
        };
        start.ArgumentList.Add(hostAssembly);
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }
        start.Environment.Remove("NGB_CONNECTION_STRING");
        start.Environment.Remove("NGB_K8S_MODE");
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return new ProcessResult(process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
