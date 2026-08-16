using System.Data;
using FluentAssertions;
using NGB.PostgreSql.Internal;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests;

public sealed class InternalSqlExecutionFullCoverageTests
{
    [Fact]
    public async Task Append_only_guard_validates_dependencies_and_builds_the_expected_command()
    {
        var connection = new RecordingDbConnection();
        var uow = new RecordingUnitOfWork(connection);

        await AssertRequired(() => PostgresAppendOnlyGuardSql.EnsureUpdateDeleteForbiddenTriggerAsync(
            null!, "events", "events_append_only", default));
        await AssertRequired(() => PostgresAppendOnlyGuardSql.EnsureUpdateDeleteForbiddenTriggerAsync(
            uow, " ", "events_append_only", default));
        await AssertRequired(() => PostgresAppendOnlyGuardSql.EnsureUpdateDeleteForbiddenTriggerAsync(
            uow, "events", " ", default));

        await PostgresAppendOnlyGuardSql.EnsureUpdateDeleteForbiddenTriggerAsync(
            uow, "audit_events", "trg_audit_events_append_only", default);

        connection.Commands.Should().ContainSingle();
        connection.Commands[0].CommandText.Should()
            .Contain("tgname = 'trg_audit_events_append_only'")
            .And.Contain("'audit_events'::regclass")
            .And.Contain("CREATE TRIGGER %I BEFORE UPDATE OR DELETE ON %I");
    }

    [Fact]
    public async Task Append_only_presence_handles_empty_input_and_materializes_missing_and_present_guards()
    {
        var table = new DataTable();
        table.Columns.Add("TableName", typeof(string));
        table.Columns.Add("HasGuard", typeof(bool));
        table.Rows.Add("guarded", true);
        var connection = new RecordingDbConnection(_ => table.CreateDataReader());
        var uow = new RecordingUnitOfWork(connection);

        var empty = await PostgresPhysicalSchemaHealthHelpers.LoadAppendOnlyGuardPresenceAsync(
            uow, [], default);
        var result = await PostgresPhysicalSchemaHealthHelpers.LoadAppendOnlyGuardPresenceAsync(
            uow, ["missing", "GUARDED"], default);

        empty.Should().BeEmpty();
        connection.Commands.Should().ContainSingle();
        connection.Commands[0].CommandText.Should().Contain("BOOL_OR").And.Contain("ANY(");
        result.Should().Contain("missing", false).And.Contain("guarded", true);
    }

    [Fact]
    public void Dimension_rules_select_restricts_dynamic_table_names()
    {
        PostgresRegisterDimensionRulesSql.SelectRulesSql(
                PostgresRegisterDimensionRulesSql.OperationalRegisterDimensionRulesTable)
            .Should().Contain("FROM operational_register_dimension_rules r");
        PostgresRegisterDimensionRulesSql.SelectRulesSql(
                PostgresRegisterDimensionRulesSql.ReferenceRegisterDimensionRulesTable)
            .Should().Contain("FROM reference_register_dimension_rules r");

        Action unsafeTable = () => PostgresRegisterDimensionRulesSql.SelectRulesSql("rules; DROP TABLE rules");
        unsafeTable.Should().Throw<NgbConfigurationViolationException>();
    }

    [Fact]
    public async Task Platform_dimension_upsert_covers_empty_insert_and_update_modes()
    {
        var connection = new RecordingDbConnection();
        var uow = new RecordingUnitOfWork(connection);
        var id = Guid.NewGuid();
        var now = new DateTime(2026, 8, 16, 1, 2, 3, DateTimeKind.Utc);

        await PostgresRegisterDimensionRulesSql.UpsertPlatformDimensionsAsync(
            uow, [], [], [], now,
            PostgresRegisterDimensionRulesSql.PlatformDimensionsUpsertMode.DoNothing, default);
        await PostgresRegisterDimensionRulesSql.UpsertPlatformDimensionsAsync(
            uow, [id], ["warehouse"], ["Warehouse"], now,
            PostgresRegisterDimensionRulesSql.PlatformDimensionsUpsertMode.DoNothing, default);
        await PostgresRegisterDimensionRulesSql.UpsertPlatformDimensionsAsync(
            uow, [id], ["warehouse"], ["Warehouse"], now,
            PostgresRegisterDimensionRulesSql.PlatformDimensionsUpsertMode.UpdateCodeAndName, default);

        connection.Commands.Should().HaveCount(2);
        connection.Commands[0].CommandText.Should().Contain("ON CONFLICT (dimension_id) DO NOTHING");
        connection.Commands[1].CommandText.Should().Contain("ON CONFLICT (dimension_id) DO UPDATE")
            .And.Contain("updated_at_utc = EXCLUDED.updated_at_utc");
    }

    [Fact]
    public async Task Register_dimension_rule_insert_covers_empty_and_every_conflict_mode()
    {
        var connection = new RecordingDbConnection();
        var uow = new RecordingUnitOfWork(connection);
        var id = Guid.NewGuid();
        var now = new DateTime(2026, 8, 16, 1, 2, 3, DateTimeKind.Utc);
        var table = PostgresRegisterDimensionRulesSql.ReferenceRegisterDimensionRulesTable;

        await PostgresRegisterDimensionRulesSql.InsertRegisterDimensionRulesAsync(
            uow, table, id, [], [], [], now,
            PostgresRegisterDimensionRulesSql.DimensionRulesConflictMode.None, default);

        foreach (var mode in new[]
                 {
                     PostgresRegisterDimensionRulesSql.DimensionRulesConflictMode.None,
                     PostgresRegisterDimensionRulesSql.DimensionRulesConflictMode.DoNothing,
                     PostgresRegisterDimensionRulesSql.DimensionRulesConflictMode.UpdateOrdinalRequired
                 })
        {
            await PostgresRegisterDimensionRulesSql.InsertRegisterDimensionRulesAsync(
                uow, table, id, [id], [0], [true], now, mode, default);
        }

        connection.Commands.Should().HaveCount(3);
        connection.Commands[0].CommandText.Should().NotContain("ON CONFLICT");
        connection.Commands[1].CommandText.Should().Contain("ON CONFLICT (register_id, dimension_id) DO NOTHING");
        connection.Commands[2].CommandText.Should().Contain("ON CONFLICT (register_id, dimension_id) DO UPDATE")
            .And.Contain("is_required = EXCLUDED.is_required");

        Func<Task> invalidMode = () => PostgresRegisterDimensionRulesSql.InsertRegisterDimensionRulesAsync(
            uow, table, id, [id], [0], [false], now,
            (PostgresRegisterDimensionRulesSql.DimensionRulesConflictMode)int.MaxValue, default);
        await invalidMode.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }

    private static async Task AssertRequired(Func<Task> act)
        => await act.Should().ThrowAsync<NgbArgumentRequiredException>();
}
