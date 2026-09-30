using FluentAssertions;
using NGB.Persistence.Migrations;
using NGB.PostgreSql.Bootstrap;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.Bootstrap;

public sealed class SchemaInitRunnerFullCoverageTests
{
    [Fact]
    public async Task Public_runner_validates_cancellation_and_none_mode_before_discovery()
    {
        Func<Task> missingConnection = () => SchemaInitRunner.RunAsync(" ", SchemaInitMode.None);
        await missingConnection.Should().ThrowAsync<NgbArgumentRequiredException>();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Func<Task> cancelled = () => SchemaInitRunner.RunAsync(
            "Host=unused",
            SchemaInitMode.None,
            ct: cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();

        var logs = new List<string>();
        await SchemaInitRunner.RunAsync("Host=unused", SchemaInitMode.None, log: logs.Add);
        logs.Should().Equal("Schema init: disabled (mode=None).");
        await SchemaInitRunner.RunAsync("Host=unused", SchemaInitMode.None);
    }

    [Fact]
    public async Task Public_non_none_mode_runs_real_assembly_discovery_and_completes_dry_run()
    {
        var logs = new List<string>();

        await SchemaInitRunner.RunAsync(
            "Host=unused",
            SchemaInitMode.Migrate,
            dryRun: true,
            log: logs.Add);

        logs.Should().Contain("Schema init plan:");
        logs.Should().Contain("Dry run: database operations are skipped.");
    }

    [Fact]
    public async Task Planned_migrate_modes_log_null_empty_and_non_empty_dependencies_and_stay_database_free_on_dry_run()
    {
        var platform = Pack("platform", dependsOn: null);
        var emptyDeps = Pack("empty", []);
        var feature = Pack("feature", ["platform"]);
        var packs = new[] { feature, emptyDeps, platform };
        var logs = new List<string>();

        await SchemaInitRunner.RunWithPacksAsync(
            "Host=unused",
            SchemaInitMode.Migrate,
            packs,
            dryRun: true,
            log: logs.Add);

        logs.Should().Contain("Schema init plan:");
        logs.Should().Contain("- platform  deps=[-]");
        logs.Should().Contain("- empty  deps=[-]");
        logs.Should().Contain("- feature  deps=[platform]");
        logs.Should().Contain("Dry run: database operations are skipped.");

        await SchemaInitRunner.RunWithPacksAsync(
            "Host=unused",
            SchemaInitMode.MigrateAndRepair,
            packs,
            includePackIds: ["feature"],
            dryRun: true,
            log: null);
    }

    [Fact]
    public async Task Repair_mode_skips_missing_delegates_honors_dry_run_and_prefers_option_aware_repair()
    {
        var calls = new List<string>();
        MigrationExecutionOptions? receivedOptions = null;
        var packs = new[]
        {
            Pack("none"),
            Pack("legacy", repair: (connectionString, _) =>
            {
                calls.Add($"legacy:{connectionString}");
                return Task.CompletedTask;
            }),
            Pack("modern", repairWithOptions: (connectionString, options, _) =>
            {
                calls.Add($"modern:{connectionString}");
                receivedOptions = options;
                return Task.CompletedTask;
            }),
            Pack(
                "both",
                repair: (_, _) => throw new InvalidOperationException("legacy must not run"),
                repairWithOptions: (_, _, _) =>
                {
                    calls.Add("both:modern");
                    return Task.CompletedTask;
                })
        };
        var options = new MigrationExecutionOptions(LockTimeout: TimeSpan.FromSeconds(3));
        var logs = new List<string>();

        await SchemaInitRunner.RunWithPacksAsync(
            "effective",
            SchemaInitMode.Repair,
            packs,
            dryRun: true,
            options: options,
            log: logs.Add);
        calls.Should().BeEmpty();
        logs.Should().Contain("Repair: legacy");

        logs.Clear();
        await SchemaInitRunner.RunWithPacksAsync(
            "effective",
            SchemaInitMode.Repair,
            packs,
            options: options,
            log: logs.Add);

        calls.Should().Equal("both:modern", "legacy:effective", "modern:effective");
        receivedOptions.Should().BeSameAs(options);
        logs.Should().Contain("Repair: modern");

        await SchemaInitRunner.RunWithPacksAsync(
            "effective",
            SchemaInitMode.Repair,
            [Pack("legacy-no-log", repair: (_, _) => Task.CompletedTask)],
            log: null);
    }

    [Fact]
    public async Task Planned_runner_rejects_unknown_mode()
    {
        Func<Task> act = () => SchemaInitRunner.RunWithPacksAsync(
            "Host=unused",
            (SchemaInitMode)999,
            [Pack("platform")]);

        await act.Should().ThrowAsync<NgbArgumentInvalidException>();
    }

    private static MigrationPack Pack(
        string id,
        IReadOnlyCollection<string>? dependsOn = null,
        Func<string, CancellationToken, Task>? repair = null,
        Func<string, MigrationExecutionOptions?, CancellationToken, Task>? repairWithOptions = null)
        => new(id, [], dependsOn, repair, repairWithOptions);
}
