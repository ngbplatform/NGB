using FluentAssertions;
using NGB.Metadata.Schema;
using NGB.PostgreSql.Schema.Internal;
using NGB.PostgreSql.Tests.TestDoubles;
using Xunit;

namespace NGB.PostgreSql.Tests.Schema;

public sealed class PostgresSchemaValidationChecksFullCoverageTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task Catalog_object_fallback_queries_catalog_and_reports_only_missing_objects(
        int matchCount,
        bool shouldReportError)
    {
        var connection = new RecordingDbConnection(scalar: _ => matchCount);
        var errors = new List<string>();

        var uow = new RecordingUnitOfWork(connection);
        var snapshot = EmptySnapshot();
        await PostgresSchemaValidationChecks.RequireFunctionAsync(
            uow,
            snapshot,
            "fn_example",
            errors,
            CancellationToken.None);
        await PostgresSchemaValidationChecks.RequireTriggerAsync(
            uow,
            snapshot,
            "tr_example",
            "example",
            errors,
            CancellationToken.None);
        await PostgresSchemaValidationChecks.RequireConstraintAsync(
            uow,
            snapshot,
            "ck_example",
            "example",
            errors,
            CancellationToken.None);

        errors.Should().HaveCount(shouldReportError ? 3 : 0);
        if (shouldReportError)
        {
            errors.Should().Equal(
                "Missing function 'fn_example'.",
                "Missing trigger 'tr_example' on 'example'.",
                "Missing constraint 'ck_example' on 'example'.");
        }

        connection.Commands.Should().HaveCount(3);
        connection.Commands.Should().ContainSingle(command =>
            command.CommandText.Contains("FROM pg_proc", StringComparison.Ordinal));
        connection.Commands.Should().ContainSingle(command =>
            command.CommandText.Contains("FROM pg_trigger", StringComparison.Ordinal));
        connection.Commands.Should().ContainSingle(command =>
            command.CommandText.Contains("FROM pg_constraint", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Bulk_object_snapshot_validates_functions_triggers_and_constraints_without_queries()
    {
        var connection = new RecordingDbConnection();
        var uow = new RecordingUnitOfWork(connection);
        var present = EmptySnapshot() with
        {
            DatabaseObjects = new DbSchemaObjectSnapshot(
                new HashSet<string>(["fn_present"], StringComparer.OrdinalIgnoreCase),
                [
                    new DbTriggerSchema("wrong", "example"),
                    new DbTriggerSchema("tr_present", "wrong"),
                    new DbTriggerSchema("tr_present", "example")
                ],
                [
                    new DbConstraintSchema("wrong", "example"),
                    new DbConstraintSchema("ck_present", "wrong"),
                    new DbConstraintSchema("ck_present", "example")
                ])
        };
        var errors = new List<string>();

        await PostgresSchemaValidationChecks.RequireFunctionAsync(uow, present, "FN_PRESENT", errors, default);
        await PostgresSchemaValidationChecks.RequireTriggerAsync(uow, present, "TR_PRESENT", "EXAMPLE", errors, default);
        await PostgresSchemaValidationChecks.RequireConstraintAsync(uow, present, "CK_PRESENT", "EXAMPLE", errors, default);
        errors.Should().BeEmpty();

        var missing = EmptySnapshot() with
        {
            DatabaseObjects = new DbSchemaObjectSnapshot(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase), [], [])
        };
        await PostgresSchemaValidationChecks.RequireFunctionAsync(uow, missing, "fn_missing", errors, default);
        await PostgresSchemaValidationChecks.RequireTriggerAsync(uow, missing, "tr_missing", "example", errors, default);
        await PostgresSchemaValidationChecks.RequireConstraintAsync(uow, missing, "ck_missing", "example", errors, default);

        errors.Should().Equal(
            "Missing function 'fn_missing'.",
            "Missing trigger 'tr_missing' on 'example'.",
            "Missing constraint 'ck_missing' on 'example'.");
        connection.Commands.Should().BeEmpty();
    }

    [Fact]
    public void Table_column_index_and_foreign_key_checks_report_only_missing_contract_parts()
    {
        var table = "example";
        var snapshot = new DbSchemaSnapshot(
            new HashSet<string>([table], StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<DbColumnSchema>>(StringComparer.OrdinalIgnoreCase)
            {
                [table] = [new DbColumnSchema(table, "id", "uuid", false, null)]
            },
            new Dictionary<string, IReadOnlyList<DbForeignKeySchema>>(StringComparer.OrdinalIgnoreCase)
            {
                [table] =
                [
                    new DbForeignKeySchema(table, "fk_example_owner", "owner_id", "owners", "id")
                ]
            },
            new Dictionary<string, IReadOnlyList<DbIndexSchema>>(StringComparer.OrdinalIgnoreCase)
            {
                [table] = [new DbIndexSchema(table, "ix_example_id", ["id"], false)]
            });
        var errors = new List<string>();

        PostgresSchemaValidationChecks.RequireTable(snapshot, "EXAMPLE", errors);
        PostgresSchemaValidationChecks.RequireTable(snapshot, "missing", errors);
        PostgresSchemaValidationChecks.RequireColumns(snapshot, table, ["ID", "missing"], errors);
        PostgresSchemaValidationChecks.RequireColumns(snapshot, "missing", ["id"], errors);
        PostgresSchemaValidationChecks.RequireIndex(snapshot, table, "IX_EXAMPLE_ID", errors);
        PostgresSchemaValidationChecks.RequireIndex(snapshot, table, "missing", errors);
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "missing", "missing", errors);
        PostgresSchemaValidationChecks.RequireForeignKey(
            snapshot, table, "OWNER_ID", "OWNERS", "ID", errors);
        PostgresSchemaValidationChecks.RequireForeignKey(
            snapshot, table, "missing", "owners", "id", errors);
        PostgresSchemaValidationChecks.RequireForeignKey(
            snapshot, "missing", "owner_id", "owners", "id", errors);

        errors.Should().Equal(
            "Missing table 'missing'.",
            "Table 'example' is missing column 'missing'.",
            "Cannot read columns for table 'missing'.",
            "Missing index 'missing' on table 'example'.",
            "Missing index 'missing' on table 'missing'.",
            "Missing foreign key: example.missing -> owners.id.",
            "Missing foreign key: missing.owner_id -> owners.id.");
    }

    private static DbSchemaSnapshot EmptySnapshot()
        => new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<DbColumnSchema>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<DbForeignKeySchema>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<DbIndexSchema>>(StringComparer.OrdinalIgnoreCase));
}
