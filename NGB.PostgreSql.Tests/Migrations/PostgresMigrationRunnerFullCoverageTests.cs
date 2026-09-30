using FluentAssertions;
using NGB.Persistence.Migrations;
using NGB.PostgreSql.Migrations;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.PostgreSql.Tests.Migrations;

public sealed class PostgresMigrationRunnerFullCoverageTests
{
    [Fact]
    public async Task Run_applies_utc_timeouts_lock_ddls_and_unlock_in_order()
    {
        var connection = new RecordingDbConnection();
        var sut = new PostgresMigrationRunner(() => connection);
        var options = new MigrationExecutionOptions(
            LockTimeout: TimeSpan.FromMilliseconds(-10),
            StatementTimeout: TimeSpan.FromMilliseconds(1500));

        await sut.RunAsync([new Ddl("first", "SELECT 1;"), new Ddl("second", "SELECT 2;")], options, default);

        connection.Commands.Select(command => command.CommandText).Should().Equal(
            "SET TIME ZONE 'UTC';",
            "SET lock_timeout = '0ms';",
            "SET statement_timeout = '1500ms';",
            "SELECT pg_advisory_lock(@key);",
            "SELECT 1;",
            "SELECT 2;",
            "SELECT pg_advisory_unlock(@key);");
    }

    [Fact]
    public async Task Run_can_skip_lock_and_options_and_handles_empty_ddl_collection()
    {
        var connection = new RecordingDbConnection();
        var sut = new PostgresMigrationRunner(() => connection);

        await sut.RunAsync([], new MigrationExecutionOptions(SkipAdvisoryLock: true), default);

        connection.Commands.Should().ContainSingle();
        connection.Commands[0].CommandText.Should().Be("SET TIME ZONE 'UTC';");

        var nullOptionsConnection = new RecordingDbConnection();
        await new PostgresMigrationRunner(() => nullOptionsConnection)
            .RunAsync([], null, default);
        nullOptionsConnection.Commands.Should().HaveCount(3);
    }

    [Fact]
    public async Task Postgres_failure_is_wrapped_with_all_diagnostics_and_flattened_sql_snippet()
    {
        var sql = new string('a', 220) + "\r\n\tBROKEN" + new string('b', 220);
        var postgres = Error(
            position: 225,
            detail: "detail",
            hint: "hint",
            internalQuery: "internal query",
            where: "function body");
        var connection = new RecordingDbConnection(nonQuery: command =>
            command == sql ? throw postgres : 1);
        var sut = new PostgresMigrationRunner(() => connection);

        Func<Task> act = () => sut.RunAsync([new Ddl("broken-ddl", sql)], null, default);

        var error = await act.Should().ThrowAsync<NgbConfigurationViolationException>();
        error.Which.InnerException.Should().BeSameAs(postgres);
        error.Which.Message.Should()
            .Contain("DDL object: broken-ddl")
            .And.Contain("SQLSTATE: 42601")
            .And.Contain("Where: function body")
            .And.Contain("Detail: detail")
            .And.Contain("Hint: hint")
            .And.Contain("InternalQuery: internal query")
            .And.Contain("^")
            .And.NotContain("\tBROKEN");
        connection.Commands.Last().CommandText.Should().Be("SELECT pg_advisory_unlock(@key);");
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("short", -1)]
    [InlineData("short", 999)]
    public async Task Failure_snippet_handles_missing_invalid_and_out_of_range_positions(
        string sql,
        int position)
    {
        var postgres = Error(position);
        var connection = new RecordingDbConnection(nonQuery: command =>
        {
            if (command == sql)
                throw postgres;

            if (command.Contains("pg_advisory_unlock", StringComparison.Ordinal))
                throw new InvalidOperationException("simulated unlock failure");

            return 1;
        });

        Func<Task> act = () => new PostgresMigrationRunner(() => connection)
            .RunAsync([new Ddl("bad", sql)], null, default);

        var error = await act.Should().ThrowAsync<NgbConfigurationViolationException>();
        if (string.IsNullOrEmpty(sql) || position <= 0)
            error.Which.Message.Should().Contain("<no SQL snippet available>");
        else
            error.Which.Message.Should().Contain("short").And.Contain("^");
        error.Which.Message.Should().NotContain("Where:").And.NotContain("Detail:").And.NotContain("Hint:")
            .And.NotContain("InternalQuery:");
    }

    private static PostgresException Error(
        int position,
        string detail = "",
        string hint = "",
        string internalQuery = "",
        string where = "")
        => new(
            "syntax error",
            "ERROR",
            "ERROR",
            "42601",
            detail,
            hint,
            position,
            0,
            internalQuery,
            where,
            "public",
            "sample",
            "column",
            "text",
            "constraint",
            "file.c",
            "123",
            "routine");

    private sealed record Ddl(string Name, string Sql) : IDdlObject
    {
        public string Generate() => Sql;
    }
}
