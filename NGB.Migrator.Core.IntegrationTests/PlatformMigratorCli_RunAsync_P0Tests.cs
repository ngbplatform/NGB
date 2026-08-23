using System.Text;
using System.Reflection;
using Dapper;
using FluentAssertions;
using NGB.Migrator.Core.IntegrationTests.Infrastructure;
using NGB.Persistence.Migrations;
using NGB.PostgreSql.Migrations.Evolve;
using Npgsql;
using Xunit;

namespace NGB.Migrator.Core.IntegrationTests;

[Collection(MigratorPostgresCollection.Name)]
public sealed class PlatformMigratorCli_RunAsync_P0Tests(MigratorPostgresFixture fixture)
{
    private static readonly SemaphoreSlim ConsoleGate = new(1, 1);

    [Fact]
    public async Task DryRun_WithInfo_And_ShowScripts_Works_Without_Connection_And_Prints_Plan()
    {
        var result = await RunCliAsync(["--dry-run", "--info", "--show-scripts"]);

        result.ExitCode.Should().Be(0);
        result.StdErr.Should().BeEmpty();
        result.StdOut.Should().Contain("Migration plan:");
        result.StdOut.Should().Contain("- platform");
        result.StdOut.Should().Contain("DryRun: True");
        result.StdOut.Should().Contain("Embedded scripts: total=");
        result.StdOut.Should().Contain("NGB.PostgreSql.db.migrations.");
    }

    [Fact]
    public async Task ListModules_Works_Without_Connection_And_Lists_Platform_Pack()
    {
        var result = await RunCliAsync(["--list-modules"]);

        result.ExitCode.Should().Be(0);
        result.StdErr.Should().BeEmpty();
        result.StdOut.Should().Contain("Discovered migration packs:");
        result.StdOut.Should().Contain("- platform");
    }

    [Fact]
    public async Task Real_Run_Without_Connection_Returns_InvalidArguments()
    {
        var result = await RunCliAsync(["--repair"]);

        result.ExitCode.Should().Be(2);
        result.StdOut.Should().BeEmpty();
        result.StdErr.Should().Contain("Missing connection string.");
    }

    [Fact]
    public void Argument_parsing_covers_flags_values_module_forms_and_numeric_boundaries()
    {
        PlatformMigratorCli.HasFlag(["--DRY-RUN"], "--dry-run").Should().BeTrue();
        PlatformMigratorCli.HasFlag(["--info"], "--dry-run").Should().BeFalse();

        PlatformMigratorCli.GetArgValue(["--connection", "value"], "--connection").Should().Be("value");
        PlatformMigratorCli.GetArgValue(["--connection"], "--connection").Should().BeNull();
        PlatformMigratorCli.GetArgValue(["--CONNECTION=inline"], "--connection").Should().Be("inline");
        PlatformMigratorCli.GetArgValue(["--other"], "--connection").Should().BeNull();

        PlatformMigratorCli.ParseModules(["--modules", " platform, demo.trade ; ;demo.crm "])
            .Should().Equal("platform", "demo.trade", "demo.crm");
        PlatformMigratorCli.ParseModules(["--module", "platform", "--MODULE", "demo.trade", "--module", " "])
            .Should().Equal("platform", "demo.trade");
        PlatformMigratorCli.ParseModules(["--module"]).Should().BeNull();
        PlatformMigratorCli.ParseModules([]).Should().BeNull();

        PlatformMigratorCli.BuildExecutionOptions([]).Should().BeNull();
        PlatformMigratorCli.BuildExecutionOptions(["--lock-timeout", "invalid", "--statement-timeout", "0"])
            .Should().BeNull();
        PlatformMigratorCli.BuildExecutionOptions(["--lock-timeout", "0", "--statement-timeout", "-1"])
            .Should().BeNull();
        PlatformMigratorCli.BuildExecutionOptions([
                "--lock-timeout-seconds=11",
                "--statement-timeout-seconds", "22"
            ])
            .Should().BeEquivalentTo(new
            {
                LockTimeout = TimeSpan.FromSeconds(11),
                StatementTimeout = TimeSpan.FromSeconds(22)
            });
    }

    [Fact]
    public void Schema_options_cover_explicit_environment_k8s_and_lock_mode_boundaries()
    {
        static string? EmptyEnvironment(string _) => null;

        var explicitOptions = PlatformMigratorCli.BuildSchemaExecutionOptions([
            "--application-name", "explicit-app",
            "--schema-lock-mode", "TRY",
            "--schema-lock-wait-seconds", "15"
        ], k8s: false, EmptyEnvironment);
        explicitOptions.ApplicationName.Should().Be("explicit-app");
        explicitOptions.LockMode.Should().Be(SchemaMigrationLockMode.Try);
        explicitOptions.LockWaitTimeout.Should().Be(TimeSpan.FromSeconds(15));

        var environment = new Dictionary<string, string?>
        {
            ["NGB_APPLICATION_NAME"] = "environment-app",
            ["NGB_SCHEMA_LOCK_MODE"] = "skip",
            ["NGB_SCHEMA_LOCK_WAIT_SECONDS"] = "25"
        };
        var environmentOptions = PlatformMigratorCli.BuildSchemaExecutionOptions(
            [],
            k8s: false,
            name => environment.GetValueOrDefault(name));
        environmentOptions.ApplicationName.Should().Be("environment-app");
        environmentOptions.LockMode.Should().Be(SchemaMigrationLockMode.Skip);
        environmentOptions.LockWaitTimeout.Should().Be(TimeSpan.FromSeconds(25));

        var blankHostname = PlatformMigratorCli.BuildSchemaExecutionOptions([], k8s: true, EmptyEnvironment);
        blankHostname.ApplicationName.Should().Be("ngb-migrator");
        blankHostname.LockMode.Should().Be(SchemaMigrationLockMode.Wait);
        blankHostname.LockWaitTimeout.Should().Be(TimeSpan.FromMinutes(30));

        var longHostname = new string('h', 40);
        var hostnameOptions = PlatformMigratorCli.BuildSchemaExecutionOptions(
            ["--schema-lock-wait", "invalid"],
            k8s: true,
            name => name == "HOSTNAME" ? longHostname : null);
        hostnameOptions.ApplicationName.Should().Be($"ngb-migrator:{new string('h', 32)}");
        hostnameOptions.LockWaitTimeout.Should().Be(TimeSpan.FromMinutes(30));

        PlatformMigratorCli.ParseLockMode(null, SchemaMigrationLockMode.Skip).Should().Be(SchemaMigrationLockMode.Skip);
        PlatformMigratorCli.ParseLockMode(" wait ", SchemaMigrationLockMode.Skip).Should().Be(SchemaMigrationLockMode.Wait);
        PlatformMigratorCli.ParseLockMode("unknown", SchemaMigrationLockMode.Try).Should().Be(SchemaMigrationLockMode.Try);
        PlatformMigratorCli.TrimMax("short", 10).Should().Be("short");
        PlatformMigratorCli.TrimMax("too-long", 3).Should().Be("too");
    }

    [Fact]
    public async Task Embedded_script_output_covers_empty_and_hidden_name_modes()
    {
        await ConsoleGate.WaitAsync();
        var originalOut = Console.Out;
        var stdout = new StringBuilder();

        try
        {
            using var writer = new StringWriter(stdout);
            Console.SetOut(writer);

            PlatformMigratorCli.PrintEmbeddedScripts([], showScripts: false);
            PlatformMigratorCli.PrintEmbeddedScripts([typeof(PlatformMigrationPackContributor).Assembly], showScripts: false);
            await writer.FlushAsync();
        }
        finally
        {
            Console.SetOut(originalOut);
            ConsoleGate.Release();
        }

        stdout.ToString().Should().Contain("Embedded scripts: <none>")
            .And.Contain("Embedded scripts: total=")
            .And.Contain("Use --show-scripts");
    }

    [Fact]
    public async Task DryRun_With_Unknown_Module_Returns_Failure()
    {
        var result = await RunCliAsync(["--dry-run", "--modules", "missing.module"]);

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("FAILED: database migration error.");
    }

    [Fact]
    public async Task DryRun_covers_environment_k8s_alias_application_name_wait_timeout_and_dependencies()
    {
        var result = await RunCliAsync(
            ["--dry-run", "--info", "--k8s", "--app-name=alias-app", "--schema-lock-wait-seconds=7"],
            k8sMode: "true",
            enableTestContributor: true);

        result.ExitCode.Should().Be(0);
        result.StdErr.Should().BeEmpty();
        result.StdOut.Should()
            .Contain("- test.feature  deps=[platform]")
            .And.Contain("ApplicationName: alias-app")
            .And.Contain("SchemaLockWaitSeconds: 7")
            .And.Contain("K8sMode: True");
    }

    [Fact]
    public async Task Contended_schema_lock_covers_skip_and_try_outcomes_without_running_migrations()
    {
        await using var db = await TemporaryDatabase.CreateAsync(fixture.ConnectionString, "ngb_migrator_lock");
        await using var owner = new NpgsqlConnection(db.ConnectionString);
        await owner.OpenAsync();
        const long schemaLockKey = 0x4E4742534348454D;
        await owner.ExecuteAsync("SELECT pg_advisory_lock(@Key);", new { Key = schemaLockKey });

        var skipped = await RunCliAsync(
            ["--connection", db.ConnectionString, "--schema-lock-mode", "skip"]);
        skipped.ExitCode.Should().Be(0);
        skipped.StdErr.Should().BeEmpty();
        skipped.StdOut.Should().Contain("OK: skipped (schema lock held by another migrator).");

        var rejected = await RunCliAsync(
            ["--connection", db.ConnectionString, "--schema-lock-mode", "try"]);
        rejected.ExitCode.Should().Be(MigratorExitCodes.LockNotAcquired);
        rejected.StdErr.Should().Contain("LOCKED: schema migration lock not acquired.");
    }

    [Fact]
    public async Task Real_Run_Migrates_Temporary_Database_And_Is_Idempotent()
    {
        await using var db = await TemporaryDatabase.CreateAsync(fixture.ConnectionString, "ngb_migrator_cli");

        var first = await RunCliAsync(["--connection", db.ConnectionString]);
        first.ExitCode.Should().Be(0);
        first.StdErr.Should().BeEmpty();
        first.StdOut.Should().Contain("OK: migrated packs: platform.");

        await using var conn = new NpgsqlConnection(db.ConnectionString);
        await conn.OpenAsync();

        var changelogExists = await conn.ExecuteScalarAsync<bool>(
            "SELECT to_regclass('public.migration_changelog__platform') IS NOT NULL;");
        changelogExists.Should().BeTrue();

        var tableExists = await conn.ExecuteScalarAsync<bool>(
            "SELECT to_regclass('public.platform_users') IS NOT NULL;");
        tableExists.Should().BeTrue();

        var countAfterFirstRun = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM public.migration_changelog__platform;");
        countAfterFirstRun.Should().BeGreaterThan(0);

        var second = await RunCliAsync(["--connection", db.ConnectionString]);
        second.ExitCode.Should().Be(0);
        second.StdErr.Should().BeEmpty();
        second.StdOut.Should().Contain("OK: migrated packs: platform.");

        var countAfterSecondRun = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM public.migration_changelog__platform;");
        countAfterSecondRun.Should().Be(countAfterFirstRun);
    }

    [Fact]
    public async Task Embedded_script_filter_ignores_wrong_prefix_and_non_sql_resources()
    {
        await ConsoleGate.WaitAsync();
        var originalOut = Console.Out;
        var stdout = new StringBuilder();

        try
        {
            using var writer = new StringWriter(stdout);
            Console.SetOut(writer);
            PlatformMigratorCli.PrintEmbeddedScripts(
                [
                    new ResourceAssembly("Ignored", ["Other.db.migrations.V001__other.sql"]),
                    new ResourceAssembly("Sample", [
                        "Other.db.migrations.V001__other.sql",
                        "Sample.db.migrations.notes.txt",
                        "Sample.db.migrations.V001__baseline.sql"
                    ])
                ],
                showScripts: true);
            await writer.FlushAsync();
        }
        finally
        {
            Console.SetOut(originalOut);
            ConsoleGate.Release();
        }

        stdout.ToString().Should().Contain("Sample.db.migrations.V001__baseline.sql")
            .And.NotContain("Other.db.migrations.V001__other.sql")
            .And.NotContain("Sample.db.migrations.notes.txt");
    }

    private static async Task<CliRunResult> RunCliAsync(
        string[] args,
        string? k8sMode = null,
        bool enableTestContributor = false)
    {
        await ConsoleGate.WaitAsync();

        var originalOut = Console.Out;
        var originalError = Console.Error;
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var originalK8sMode = Environment.GetEnvironmentVariable("NGB_K8S_MODE");

        try
        {
            Environment.SetEnvironmentVariable("NGB_K8S_MODE", k8sMode);
            TestMigrationPackContributor.Enabled = enableTestContributor;
            using var stdoutWriter = new StringWriter(stdout);
            using var stderrWriter = new StringWriter(stderr);

            Console.SetOut(stdoutWriter);
            Console.SetError(stderrWriter);

            var exitCode = await PlatformMigratorCli.RunAsync(args);

            await stdoutWriter.FlushAsync();
            await stderrWriter.FlushAsync();

            return new CliRunResult(exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            TestMigrationPackContributor.Enabled = false;
            Environment.SetEnvironmentVariable("NGB_K8S_MODE", originalK8sMode);
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            ConsoleGate.Release();
        }
    }

    public sealed class TestMigrationPackContributor : IMigrationPackContributor
    {
        public static bool Enabled { get; set; }

        public IEnumerable<MigrationPack> GetPacks()
            => Enabled
                ? [new MigrationPack("test.feature", [], ["platform"])]
                : [];
    }

    private sealed class ResourceAssembly(string name, string[] resources) : Assembly
    {
        public override AssemblyName GetName(bool copiedName) => new(name);
        public override string[] GetManifestResourceNames() => resources;
    }

    private sealed record CliRunResult(int ExitCode, string StdOut, string StdErr);
}
