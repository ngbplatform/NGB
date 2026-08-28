using System.Reflection;
using FluentAssertions;
using NGB.Persistence.Migrations;
using NGB.PostgreSql.Migrations.Evolve;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.PostgreSql.Tests.Migrations;

public sealed class PostgresEvolveMigratorFullCoverageTests
{
    [Fact]
    public async Task Connection_string_overload_validates_before_opening_a_database_connection()
    {
        var assembly = typeof(PostgresEvolveMigratorFullCoverageTests).Assembly;
        Func<Task> missingConnection = () => PostgresEvolveMigrator.MigrateAsync(" ", [assembly]);
        Func<Task> missingAssemblies = () => PostgresEvolveMigrator.MigrateAsync("Host=localhost", null!);
        Func<Task> emptyAssemblies = () => PostgresEvolveMigrator.MigrateAsync("Host=localhost", []);
        await missingConnection.Should().ThrowAsync<NgbArgumentRequiredException>();
        await missingAssemblies.Should().ThrowAsync<NgbArgumentInvalidException>();
        await emptyAssemblies.Should().ThrowAsync<NgbArgumentInvalidException>();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Func<Task> cancelled = () => PostgresEvolveMigrator.MigrateAsync(
            "Host=localhost",
            [assembly],
            ct: cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Existing_connection_overload_validates_before_running_evolve()
    {
        var assembly = typeof(PostgresEvolveMigratorFullCoverageTests).Assembly;
        using var connection = new NpgsqlConnection();
        Func<Task> missingConnection = () => PostgresEvolveMigrator.MigrateAsync((NpgsqlConnection)null!, [assembly]);
        Func<Task> missingAssemblies = () => PostgresEvolveMigrator.MigrateAsync(connection, null!);
        Func<Task> emptyAssemblies = () => PostgresEvolveMigrator.MigrateAsync(connection, []);
        await missingConnection.Should().ThrowAsync<NgbArgumentRequiredException>();
        await missingAssemblies.Should().ThrowAsync<NgbArgumentInvalidException>();
        await emptyAssemblies.Should().ThrowAsync<NgbArgumentInvalidException>();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Func<Task> cancelled = () => PostgresEvolveMigrator.MigrateAsync(
            connection,
            [assembly],
            ct: cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Resource_filters_are_case_insensitively_distinct_and_include_versioned_and_repeatable_prefixes()
    {
        var alpha = new ResourceAssembly("Alpha", []);
        var beta = new ResourceAssembly("Beta", []);

        var filters = PostgresEvolveMigrator.BuildEmbeddedResourceFilters([alpha, beta, alpha]);

        filters.Should().Equal(
            "Alpha.db.migrations.V",
            "Alpha.db.migrations.R",
            "Beta.db.migrations.V",
            "Beta.db.migrations.R");
    }

    [Fact]
    public void Log_forwarder_supports_absent_and_present_callbacks()
    {
        var messages = new List<string>();

        PostgresEvolveMigrator.BuildLogForwarder(null)("ignored");
        PostgresEvolveMigrator.BuildLogForwarder(messages.Add)("migration applied");

        messages.Should().Equal("migration applied");
    }

    [Fact]
    public void Metadata_identifiers_use_fallbacks_only_for_absent_values()
    {
        PostgresEvolveMigrator.ResolveMetadataIdentifier(null, "fallback").Should().Be("fallback");
        PostgresEvolveMigrator.ResolveMetadataIdentifier(" ", "fallback").Should().Be("fallback");
        PostgresEvolveMigrator.ResolveMetadataIdentifier("custom", "fallback").Should().Be("custom");
    }

    [Fact]
    public void Resource_discovery_accepts_matching_sql_and_ignores_other_resources()
    {
        var alpha = new ResourceAssembly(
            "Alpha",
            [
                "Alpha.db.migrations.notes.txt",
                "Alpha.db.migrations.archive.V001__nested.sql",
                "Alpha.db.migrations.v001__baseline.SQL"
            ]);
        var beta = new ResourceAssembly("Beta", ["Beta.db.migrations.R__view.sql"]);
        var filters = PostgresEvolveMigrator.BuildEmbeddedResourceFilters([alpha, beta]);

        Action act = () => PostgresEvolveMigrator.EnsureEmbeddedMigrationsDiscovered([alpha, beta], filters);

        act.Should().NotThrow();
    }

    [Fact]
    public void Resource_discovery_reports_a_bounded_sorted_sql_sample_and_handles_broken_metadata()
    {
        var resources = Enumerable.Range(0, 35)
            .Reverse()
            .Select(index => $"Broken.other.{index:00}.sql")
            .Append("Broken.db.migrations.V001__not_sql.txt")
            .ToArray();
        var missing = new ResourceAssembly("Broken", resources);
        var filters = PostgresEvolveMigrator.BuildEmbeddedResourceFilters([missing]);

        Action noMatches = () => PostgresEvolveMigrator.EnsureEmbeddedMigrationsDiscovered([missing], filters);
        var error = noMatches.Should().Throw<NgbInvariantViolationException>().Which;
        var sample = error.Context["sqlResourceSample"].Should().BeAssignableTo<string[]>().Subject;
        sample.Should().HaveCount(30);
        sample.Should().BeInAscendingOrder(StringComparer.Ordinal);

        var faulty = new FaultyResourceAssembly("Faulty", new InvalidOperationException("metadata failure"));
        PostgresEvolveMigrator.SafeGetManifestResourceNames(faulty).Should().BeEmpty();
        Action faultyDiscovery = () => PostgresEvolveMigrator.EnsureEmbeddedMigrationsDiscovered(
            [faulty],
            PostgresEvolveMigrator.BuildEmbeddedResourceFilters([faulty]));
        faultyDiscovery.Should().Throw<NgbInvariantViolationException>();

        var unnamed = new UnnamedResourceAssembly();
        Action unnamedDiscovery = () => PostgresEvolveMigrator.EnsureEmbeddedMigrationsDiscovered(
            [unnamed],
            ["Unnamed.db.migrations.V"]);
        unnamedDiscovery.Should().Throw<NgbInvariantViolationException>().WithMessage("*'<unknown>'*");
    }

    [Fact]
    public async Task Session_defaults_always_set_utc_and_apply_each_optional_clamped_timeout()
    {
        var defaults = new RecordingDbConnection();
        defaults.Open();
        await PostgresEvolveMigrator.ApplySessionDefaultsAsync(defaults, null);
        defaults.Commands.Select(command => command.CommandText).Should().Equal("SET TIME ZONE 'UTC';");

        var lockOnly = new RecordingDbConnection();
        lockOnly.Open();
        await PostgresEvolveMigrator.ApplySessionDefaultsAsync(
            lockOnly,
            new MigrationExecutionOptions(LockTimeout: TimeSpan.FromMilliseconds(-10)));
        lockOnly.Commands.Select(command => command.CommandText).Should().Equal(
            "SET TIME ZONE 'UTC';",
            "SET lock_timeout = '0ms';");

        var statementOnly = new RecordingDbConnection();
        statementOnly.Open();
        await PostgresEvolveMigrator.ApplySessionDefaultsAsync(
            statementOnly,
            new MigrationExecutionOptions(StatementTimeout: TimeSpan.FromMilliseconds(1500)));
        statementOnly.Commands.Select(command => command.CommandText).Should().Equal(
            "SET TIME ZONE 'UTC';",
            "SET statement_timeout = '1500ms';");

        var both = new RecordingDbConnection();
        both.Open();
        await PostgresEvolveMigrator.ApplySessionDefaultsAsync(
            both,
            new MigrationExecutionOptions(
                LockTimeout: TimeSpan.FromMilliseconds(10),
                StatementTimeout: TimeSpan.FromMilliseconds(-1)));
        both.Commands.Select(command => command.CommandText).Should().Equal(
            "SET TIME ZONE 'UTC';",
            "SET lock_timeout = '10ms';",
            "SET statement_timeout = '0ms';");
    }

    private sealed class ResourceAssembly(string name, string[] resources) : Assembly
    {
        public override AssemblyName GetName(bool copiedName) => new(name);
        public override string[] GetManifestResourceNames() => resources;
    }

    private sealed class FaultyResourceAssembly(string name, Exception error) : Assembly
    {
        public override AssemblyName GetName(bool copiedName) => new(name);
        public override string[] GetManifestResourceNames() => throw error;
    }

    private sealed class UnnamedResourceAssembly : Assembly
    {
        public override AssemblyName GetName(bool copiedName) => new();
        public override string[] GetManifestResourceNames() => [];
    }
}
