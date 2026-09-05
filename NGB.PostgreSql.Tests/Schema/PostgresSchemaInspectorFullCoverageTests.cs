using System.Data;
using FluentAssertions;
using NGB.PostgreSql.Schema;
using NGB.PostgreSql.Tests.TestDoubles;
using Xunit;

namespace NGB.PostgreSql.Tests.Schema;

public sealed class PostgresSchemaInspectorFullCoverageTests
{
    [Fact]
    public async Task Snapshot_scope_releases_gate_when_loading_fails()
    {
        var sut = new PostgresSchemaInspector(new RecordingUnitOfWork(
            new RecordingDbConnection(readerFactory: _ => throw new InvalidOperationException("snapshot failed"))));

        await ((Func<Task>)(async () => await sut.BeginSnapshotScopeAsync()))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("snapshot failed");
        await ((Func<Task>)(async () => await sut.BeginSnapshotScopeAsync()))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("snapshot failed");
    }

    [Fact]
    public async Task Snapshot_scope_reuses_loaded_snapshot_and_disposal_is_idempotent()
    {
        var connection = new RecordingDbConnection(readerFactory: _ => SnapshotResults());
        var sut = new PostgresSchemaInspector(new RecordingUnitOfWork(connection));

        var scope = await sut.BeginSnapshotScopeAsync();
        var first = await sut.GetSnapshotAsync();
        var second = await sut.GetSnapshotAsync();

        first.Should().BeSameAs(second);
        first.Tables.Should().Contain("documents");
        connection.Commands.Should().ContainSingle();

        await scope.DisposeAsync();
        await scope.DisposeAsync();
    }

    private static DataTableReader SnapshotResults()
    {
        var tables = Table(("table_name", typeof(string)));
        tables.Rows.Add("documents");

        var columns = Table(
            ("TableName", typeof(string)),
            ("ColumnName", typeof(string)),
            ("DbType", typeof(string)),
            ("IsNullable", typeof(bool)),
            ("CharacterMaximumLength", typeof(int)));
        var foreignKeys = Table(
            ("TableName", typeof(string)),
            ("ConstraintName", typeof(string)),
            ("ColumnName", typeof(string)),
            ("ReferencedTableName", typeof(string)),
            ("ReferencedColumnName", typeof(string)));
        var indexes = Table(
            ("tablename", typeof(string)),
            ("indexname", typeof(string)),
            ("isunique", typeof(bool)),
            ("columnnames", typeof(string[])));
        var functions = Table(("proname", typeof(string)));
        var triggers = Table(("TriggerName", typeof(string)), ("TableName", typeof(string)));
        var constraints = Table(("ConstraintName", typeof(string)), ("TableName", typeof(string)));

        return new DataTableReader([tables, columns, foreignKeys, indexes, functions, triggers, constraints]);
    }

    private static DataTable Table(params (string Name, Type Type)[] columns)
    {
        var table = new DataTable();
        foreach (var (name, type) in columns)
            table.Columns.Add(name, type);
        return table;
    }
}
