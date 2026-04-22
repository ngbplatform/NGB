using System.Text;
using Dapper;
using FluentAssertions;
using NGB.Migrator.Core.IntegrationTests.Infrastructure;
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

    private static async Task<CliRunResult> RunCliAsync(string[] args)
    {
        await ConsoleGate.WaitAsync();

        var originalOut = Console.Out;
        var originalError = Console.Error;
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        try
        {
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
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            ConsoleGate.Release();
        }
    }

    private sealed record CliRunResult(int ExitCode, string StdOut, string StdErr);
}
