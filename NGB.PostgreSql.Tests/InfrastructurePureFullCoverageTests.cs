using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using FluentAssertions;
using NGB.PostgreSql.Dapper;
using NGB.PostgreSql.Internal;
using NGB.PostgreSql.Migrations.Evolve;
using NGB.Tools.Exceptions;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace NGB.PostgreSql.Tests;

public sealed class InfrastructurePureFullCoverageTests
{
    [Fact]
    public void Date_only_handler_sets_native_and_generic_parameters_and_parses_supported_values()
    {
        var sut = new DateOnlyTypeHandler();
        var value = new DateOnly(2026, 8, 16);
        var native = new NpgsqlParameter();
        var generic = new StubParameter();

        sut.SetValue(native, value);
        sut.SetValue(generic, value);

        native.NpgsqlDbType.Should().Be(NpgsqlDbType.Date);
        native.Value.Should().Be(value.ToDateTime(TimeOnly.MinValue));
        generic.DbType.Should().Be(DbType.Date);
        generic.Value.Should().Be(value.ToDateTime(TimeOnly.MinValue));
        sut.Parse(value).Should().Be(value);
        sut.Parse(value.ToDateTime(new TimeOnly(12, 30))).Should().Be(value);
        sut.Parse("2026-08-16").Should().Be(value);
    }

    [Fact]
    public void PostgreSql_identifier_guard_accepts_boundaries_and_rejects_empty_long_and_unsafe_values()
    {
        PostgresSqlIdentifiers.EnsureOrThrow("_safe_123", "test");
        PostgresSqlIdentifiers.EnsureOrThrow(new string('a', PostgresSqlIdentifiers.MaxIdentifierLength), "test");

        foreach (var invalid in new string?[]
                 {
                     null,
                     string.Empty,
                     " ",
                     new string('a', PostgresSqlIdentifiers.MaxIdentifierLength + 1),
                     "UpperCase",
                     "starts-with-dash"
                 })
        {
            Action act = () => PostgresSqlIdentifiers.EnsureOrThrow(invalid!, "unit-test");
            act.Should().Throw<NgbConfigurationViolationException>()
                .Which.Context.Should().Contain("context", "unit-test");
        }
    }

    [Fact]
    public void Migration_lock_exception_and_execution_options_expose_complete_structured_context()
    {
        var timeout = TimeSpan.FromSeconds(12.5);
        var withTimeout = new SchemaMigrationLockNotAcquiredException(SchemaMigrationLockMode.Try, timeout);
        withTimeout.ErrorCode.Should().Be(SchemaMigrationLockNotAcquiredException.Code);
        withTimeout.Mode.Should().Be(SchemaMigrationLockMode.Try);
        withTimeout.WaitTimeout.Should().Be(timeout);
        withTimeout.Context.Should().Contain("mode", "Try")
            .And.Contain("waitTimeoutSeconds", 12.5d)
            .And.ContainKey("lockKey");

        var withoutTimeout = new SchemaMigrationLockNotAcquiredException(SchemaMigrationLockMode.Skip, null);
        withoutTimeout.WaitTimeout.Should().BeNull();
        withoutTimeout.Context["waitTimeoutSeconds"].Should().BeNull();

        var defaults = new SchemaMigrationExecutionOptions();
        defaults.ApplicationName.Should().BeNull();
        defaults.LockMode.Should().Be(SchemaMigrationLockMode.Wait);
        defaults.LockWaitTimeout.Should().BeNull();
        var configured = new SchemaMigrationExecutionOptions("migrator", SchemaMigrationLockMode.Try, timeout);
        configured.Should().BeEquivalentTo(new
        {
            ApplicationName = "migrator",
            LockMode = SchemaMigrationLockMode.Try,
            LockWaitTimeout = (TimeSpan?)timeout
        });
    }

    [Fact]
    public void Migration_assembly_discovery_covers_default_null_duplicate_success_and_load_failure_paths()
    {
        var current = typeof(InfrastructurePureFullCoverageTests).Assembly;
        MigrationAssemblyDiscovery.LoadForPackDiscovery(current).Should().Contain(current);
        MigrationAssemblyDiscovery.LoadForPackDiscovery().Should().NotBeEmpty();

        MigrationAssemblyDiscovery.LoadForPackDiscoveryCore(
                null,
                [current, current],
                Assembly.Load)
            .Should().ContainSingle().Which.Should().BeSameAs(current);

        MigrationAssemblyDiscovery.LoadForPackDiscoveryCore(
                current,
                Array.Empty<Assembly>(),
                _ => current)
            .Should().ContainSingle().Which.Should().BeSameAs(current);

        MigrationAssemblyDiscovery.LoadForPackDiscoveryCore(
                current,
                Array.Empty<Assembly>(),
                _ => throw new FileNotFoundException("simulated optional dependency"))
            .Should().BeEmpty();
    }

    private sealed class StubParameter : IDbDataParameter
    {
        public DbType DbType { get; set; }
        public ParameterDirection Direction { get; set; }
        public bool IsNullable => true;
        [AllowNull]
        public string ParameterName { get; set; } = string.Empty;

        [AllowNull]
        public string SourceColumn { get; set; } = string.Empty;
        public DataRowVersion SourceVersion { get; set; }
        public object? Value { get; set; }
        public byte Precision { get; set; }
        public byte Scale { get; set; }
        public int Size { get; set; }
    }
}
