using System.Reflection;
using FluentAssertions;
using NGB.Persistence.Migrations;
using NGB.PostgreSql.Migrations.Evolve;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.Migrations;

public sealed class SchemaMigratorFullCoverageTests
{
    [Fact]
    public void DiscoverPacks_validates_filters_faulty_assemblies_and_de_duplicates_search_list()
    {
        Action missing = () => SchemaMigrator.DiscoverPacks(null!);
        missing.Should().Throw<NgbArgumentInvalidException>();

        var assembly = new StubAssembly(
            [
                null!,
                typeof(AbstractContributor),
                typeof(IMigrationPackContributor),
                typeof(string),
                typeof(AlphaContributor),
                typeof(BetaContributor)
            ]);
        var packs = SchemaMigrator.DiscoverPacks([assembly, assembly]);
        packs.Select(pack => pack.Id).Should().BeEquivalentTo("alpha", "beta");

        var partial = new FaultyAssembly(
            new ReflectionTypeLoadException(
                [typeof(AlphaContributor), null],
                [new TypeLoadException("missing dependency"), new TypeLoadException("missing type")]));
        SchemaMigrator.DiscoverPacks([partial]).Should().ContainSingle(pack => pack.Id == "alpha");

        var broken = new FaultyAssembly(new InvalidOperationException("broken metadata"));
        SchemaMigrator.DiscoverPacks([broken]).Should().BeEmpty();
    }

    [Fact]
    public void DiscoverPacks_reports_all_duplicate_ids_in_deterministic_order()
    {
        Action act = () => SchemaMigrator.ValidateUniquePacks(
            [Pack("z"), Pack("Z"), Pack("a"), Pack("A")]);

        var error = act.Should().Throw<NgbInvariantViolationException>().Which;
        error.Message.Should().Contain("a, z");
    }

    [Fact]
    public void Plan_validates_input_resolves_dependencies_filters_blanks_and_orders_deterministically()
    {
        Action missing = () => SchemaMigrator.Plan(null!);
        Action empty = () => SchemaMigrator.Plan([]);
        missing.Should().Throw<NgbArgumentInvalidException>();
        empty.Should().Throw<NgbArgumentInvalidException>();

        var platform = Pack("platform", dependsOn: null);
        var shared = Pack("shared");
        var feature = Pack("feature", [" ", "SHARED", "platform", "shared"]);
        var addon = Pack("addon");
        var discovered = new[] { feature, shared, platform, addon };

        var selected = SchemaMigrator.Plan(discovered, [" ", "FEATURE"]);
        selected.Select(pack => pack.Id).Should().ContainInOrder("platform", "shared", "feature");
        selected.Should().NotContain(pack => pack.Id == "addon");

        var all = SchemaMigrator.Plan(discovered);
        all.Should().HaveCount(4);
        var allIds = all.Select(pack => pack.Id).ToList();
        allIds.IndexOf(platform.Id).Should().BeLessThan(allIds.IndexOf(feature.Id));
        allIds.IndexOf(shared.Id).Should().BeLessThan(allIds.IndexOf(feature.Id));

        SchemaMigrator.Plan(discovered, []).Should().HaveCount(4);
        SchemaMigrator.Plan(discovered, [" "]).Should().BeEmpty();
    }

    [Fact]
    public void Plan_rejects_unknown_ids_and_dependency_cycles()
    {
        Action unknown = () => SchemaMigrator.Plan([Pack("known")], ["unknown"]);
        unknown.Should().Throw<NgbArgumentInvalidException>();

        var first = Pack("first", ["second"]);
        var second = Pack("second", ["first"]);
        Action cycle = () => SchemaMigrator.Plan([first, second]);
        var error = cycle.Should().Throw<NgbInvariantViolationException>().Which;
        error.Context["cyclePackIds"].Should().BeEquivalentTo(new[] { "first", "second" });
    }

    [Fact]
    public async Task Dry_run_validates_cancellation_and_returns_the_selected_plan_without_database_access()
    {
        var pack = Pack("platform");
        Func<Task> missingConnection = () => SchemaMigrator.MigrateAsync(null, [pack]);
        Func<Task> missingPacks = () => SchemaMigrator.MigrateAsync(null, null!, dryRun: true);
        Func<Task> emptyPacks = () => SchemaMigrator.MigrateAsync(null, [], dryRun: true);
        await missingConnection.Should().ThrowAsync<NgbArgumentRequiredException>();
        await missingPacks.Should().ThrowAsync<NgbArgumentInvalidException>();
        await emptyPacks.Should().ThrowAsync<NgbArgumentInvalidException>();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Func<Task> cancelled = () => SchemaMigrator.MigrateAsync(null, [pack], dryRun: true, ct: cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();

        var logs = new List<string>();
        var result = await SchemaMigrator.MigrateAsync(null, [pack], dryRun: true, log: logs.Add);
        result.Should().Equal(pack);
        logs.Should().Equal("Dry run: database operations are skipped.");
        (await SchemaMigrator.MigrateAsync(null, [pack], dryRun: true)).Should().Equal(pack);
    }

    [Fact]
    public void Connection_string_builder_applies_only_non_empty_application_name()
    {
        const string connectionString = "Host=localhost;Database=ngb;Username=user;Password=secret";
        SchemaMigrator.BuildConnectionString(connectionString, null).Should().NotContain("Application Name");
        SchemaMigrator.BuildConnectionString(
                connectionString,
                new SchemaMigrationExecutionOptions(ApplicationName: string.Empty))
            .Should().NotContain("Application Name");
        SchemaMigrator.BuildConnectionString(
                connectionString,
                new SchemaMigrationExecutionOptions(ApplicationName: "ngb-migrator"))
            .Should().Contain("Application Name=ngb-migrator");
    }

    [Fact]
    public async Task Repairs_skip_empty_packs_prefer_option_aware_delegate_and_force_lock_bypass()
    {
        var calls = new List<string>();
        MigrationExecutionOptions? receivedOptions = null;
        var noRepair = Pack("none");
        var legacy = Pack(
            "legacy",
            repair: (connectionString, _) =>
            {
                calls.Add($"legacy:{connectionString}");
                return Task.CompletedTask;
            });
        var modern = Pack(
            "modern",
            repairWithOptions: (connectionString, options, _) =>
            {
                calls.Add($"modern:{connectionString}");
                receivedOptions = options;
                return Task.CompletedTask;
            });
        var both = Pack(
            "both",
            repair: (_, _) => throw new InvalidOperationException("legacy must not win"),
            repairWithOptions: (_, _, _) =>
            {
                calls.Add("both:modern");
                return Task.CompletedTask;
            });
        var logs = new List<string>();

        await SchemaMigrator.RunRepairsAsync(
            [noRepair, legacy, modern, both],
            "effective",
            null,
            logs.Add,
            default);

        calls.Should().Equal("legacy:effective", "modern:effective", "both:modern");
        receivedOptions.Should().NotBeNull();
        receivedOptions!.SkipAdvisoryLock.Should().BeTrue();
        logs.Should().Equal("Repair: legacy", "Repair: modern", "Repair: both");

        var configured = new MigrationExecutionOptions(
            LockTimeout: TimeSpan.FromSeconds(1),
            StatementTimeout: TimeSpan.FromSeconds(2));
        await SchemaMigrator.RunRepairsAsync(
            [Pack("configured", repairWithOptions: (_, options, _) =>
            {
                receivedOptions = options;
                return Task.CompletedTask;
            })],
            "effective",
            configured,
            log: null,
            ct: default);
        receivedOptions.Should().Be(configured with { SkipAdvisoryLock = true });
    }

    [Fact]
    public async Task Session_options_clamp_negative_values_and_unlock_is_best_effort()
    {
        var connection = new RecordingDbConnection();
        connection.Open();
        await SchemaMigrator.ApplySessionOptionsAsync(connection, null, default);
        connection.Commands.Should().BeEmpty();

        await SchemaMigrator.ApplySessionOptionsAsync(
            connection,
            new MigrationExecutionOptions(
                LockTimeout: TimeSpan.FromMilliseconds(-1),
                StatementTimeout: TimeSpan.FromMilliseconds(2500)),
            default);
        connection.Commands.Select(command => command.CommandText).Should().ContainInOrder(
            "SET lock_timeout = '0ms';",
            "SET statement_timeout = '2500ms';");

        var released = false;
        await SchemaMigrator.ReleaseLockBestEffortAsync(() =>
        {
            released = true;
            return Task.CompletedTask;
        });
        released.Should().BeTrue();
        await SchemaMigrator.ReleaseLockBestEffortAsync(
            () => Task.FromException(new InvalidOperationException("connection already closed")));
    }

    [Fact]
    public void Pack_identifier_normalization_handles_empty_symbols_compaction_and_stable_truncation()
    {
        SchemaMigrator.NormalizePackIdToIdentifier(string.Empty).Should().Be("pack");
        SchemaMigrator.NormalizePackIdToIdentifier("---").Should().Be("pack");
        SchemaMigrator.NormalizePackIdToIdentifier("  CRM---Billing__V2!  ").Should().Be("crm_billing_v2");
        SchemaMigrator.GetPackChangelogTableName("CRM Billing").Should().Be("migration_changelog__crm_billing");

        var longId = string.Concat(Enumerable.Repeat("Very-Long-Pack-", 10));
        var normalized = SchemaMigrator.NormalizePackIdToIdentifier(longId);
        normalized.Length.Should().Be(42);
        normalized.Should().MatchRegex("^[a-z0-9_]+_[a-f0-9]{12}$");
        SchemaMigrator.NormalizePackIdToIdentifier(longId).Should().Be(normalized);
    }

    private static MigrationPack Pack(
        string id,
        IReadOnlyCollection<string>? dependsOn = null,
        Func<string, CancellationToken, Task>? repair = null,
        Func<string, MigrationExecutionOptions?, CancellationToken, Task>? repairWithOptions = null)
        => new(id, [], dependsOn, repair, repairWithOptions);

    public sealed class AlphaContributor : IMigrationPackContributor
    {
        public IEnumerable<MigrationPack> GetPacks() => [Pack("alpha")];
    }

    public sealed class BetaContributor : IMigrationPackContributor
    {
        public IEnumerable<MigrationPack> GetPacks() => [Pack("beta")];
    }

    public abstract class AbstractContributor : IMigrationPackContributor
    {
        public abstract IEnumerable<MigrationPack> GetPacks();
    }

    private sealed class StubAssembly(Type[] types) : Assembly
    {
        public override Type[] GetTypes() => types;
    }

    private sealed class FaultyAssembly(Exception error) : Assembly
    {
        public override Type[] GetTypes() => throw error;
    }
}
