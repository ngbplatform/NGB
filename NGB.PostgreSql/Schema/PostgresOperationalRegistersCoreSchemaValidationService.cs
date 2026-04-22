using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NGB.Persistence.Schema;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Schema.Internal;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Schema;

/// <summary>
/// PostgreSQL validator for the Operational Registers core schema.
///
/// This validator is intentionally conservative: it fails fast when the platform
/// can't safely run (correctness + safety invariants for append-only registers).
/// </summary>
public sealed class PostgresOperationalRegistersCoreSchemaValidationService(
    IDbSchemaInspector schemaInspector,
    IUnitOfWork uow,
    ILogger<PostgresOperationalRegistersCoreSchemaValidationService> logger)
    : IOperationalRegistersCoreSchemaValidationService
{
    public async Task ValidateAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var snapshot = await schemaInspector.GetSnapshotAsync(ct);
        var errors = new List<string>();

        // 1) Core metadata tables
        PostgresSchemaValidationChecks.RequireTable(snapshot, "operational_registers", errors);
        PostgresSchemaValidationChecks.RequireTable(snapshot, "operational_register_resources", errors);
        PostgresSchemaValidationChecks.RequireTable(snapshot, "operational_register_dimension_rules", errors);
        PostgresSchemaValidationChecks.RequireTable(snapshot, "operational_register_finalizations", errors);
        PostgresSchemaValidationChecks.RequireTable(snapshot, "operational_register_write_state", errors);

        // Required platform tables for FKs
        PostgresSchemaValidationChecks.RequireTable(snapshot, "platform_dimensions", errors);
        PostgresSchemaValidationChecks.RequireTable(snapshot, "platform_dimension_sets", errors);
        PostgresSchemaValidationChecks.RequireTable(snapshot, "documents", errors);

        // 2) Minimal column contracts
        PostgresSchemaValidationChecks.RequireColumns(
            snapshot,
            tableName: "operational_registers",
            required:
            [
                "register_id",
                "code",
                "code_norm",
                "name",
                "table_code",
                "has_movements"
            ],
            errors);

        PostgresSchemaValidationChecks.RequireColumns(
            snapshot,
            tableName: "operational_register_resources",
            required:
            [
                "register_id",
                "code",
                "code_norm",
                "column_code",
                "name",
                "ordinal"
            ],
            errors);

        PostgresSchemaValidationChecks.RequireColumns(
            snapshot,
            tableName: "operational_register_dimension_rules",
            required:
            [
                "register_id",
                "dimension_id",
                "ordinal",
                "is_required"
            ],
            errors);

        PostgresSchemaValidationChecks.RequireColumns(
            snapshot,
            tableName: "operational_register_finalizations",
            required:
            [
                "register_id",
                "period",
                "status"
            ],
            errors);

        PostgresSchemaValidationChecks.RequireColumns(
            snapshot,
            tableName: "operational_register_write_state",
            required:
            [
                "register_id",
                "document_id",
                "operation",
                "started_at_utc",
                "completed_at_utc"
            ],
            errors);

        // 3) Critical indexes (names are part of the contract; migrations are idempotent)
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "operational_registers", "ux_operational_registers_code_norm", errors);
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "operational_registers", "ux_operational_registers_table_code", errors);

        PostgresSchemaValidationChecks.RequireIndex(snapshot, "operational_register_resources", "ix_opreg_resources_register_ordinal", errors);
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "operational_register_dimension_rules", "ix_opreg_dim_rules_register_ordinal", errors);
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "operational_register_finalizations", "ix_opreg_finalizations_register_period", errors);
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "operational_register_write_state", "ix_opreg_write_log_document", errors);

        // 4) Critical unique constraints (surfaced as indexes)
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "operational_register_resources", "ux_operational_register_resources__register_code_norm", errors);
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "operational_register_resources", "ux_operational_register_resources__register_ordinal", errors);
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "operational_register_dimension_rules", "ux_opreg_dim_rules__register_ordinal", errors);

        // 5) Critical foreign keys
        PostgresSchemaValidationChecks.RequireForeignKey(snapshot, "operational_register_resources", "register_id", "operational_registers", "register_id", errors);
        PostgresSchemaValidationChecks.RequireForeignKey(snapshot, "operational_register_dimension_rules", "register_id", "operational_registers", "register_id", errors);
        PostgresSchemaValidationChecks.RequireForeignKey(snapshot, "operational_register_dimension_rules", "dimension_id", "platform_dimensions", "dimension_id", errors);
        PostgresSchemaValidationChecks.RequireForeignKey(snapshot, "operational_register_finalizations", "register_id", "operational_registers", "register_id", errors);
        PostgresSchemaValidationChecks.RequireForeignKey(snapshot, "operational_register_write_state", "register_id", "operational_registers", "register_id", errors);
        PostgresSchemaValidationChecks.RequireForeignKey(snapshot, "operational_register_write_state", "document_id", "documents", "id", errors);

        // 6) DB-level guards and invariants
        await uow.EnsureConnectionOpenAsync(ct);

        // 6.1) Append-only guard function must exist (used by per-register movements tables)
        await PostgresSchemaValidationChecks.RequireFunctionAsync(uow, "ngb_forbid_mutation_of_append_only_table", errors, ct);

        // 6.2) Resource immutability after movements exist (defense in depth)
        await PostgresSchemaValidationChecks.RequireFunctionAsync(uow, "ngb_opreg_forbid_resource_mutation_when_has_movements", errors, ct);
        await PostgresSchemaValidationChecks.RequireTriggerAsync(uow, "trg_opreg_resources_immutable_when_has_movements", "operational_register_resources", errors, ct);

        // 6.3) Register immutability after movements exist (code/table identity safety)
        await PostgresSchemaValidationChecks.RequireFunctionAsync(uow, "ngb_opreg_forbid_register_mutation_when_has_movements", errors, ct);
        await PostgresSchemaValidationChecks.RequireTriggerAsync(uow, "trg_opreg_registers_immutable_when_has_movements", "operational_registers", errors, ct);

        // 6.4) Dimension rules guards after movements exist (prevent destructive mutation)
        await PostgresSchemaValidationChecks.RequireFunctionAsync(uow, "ngb_opreg_forbid_dim_rule_mutation_when_has_movements", errors, ct);
        await PostgresSchemaValidationChecks.RequireTriggerAsync(uow, "trg_opreg_dim_rules_immutable_when_has_movements", "operational_register_dimension_rules", errors, ct);

        if (errors.Count > 0)
        {
            logger.LogError(
                "Operational registers core schema validation FAILED with {ErrorCount} errors in {ElapsedMs} ms.",
                errors.Count,
                sw.ElapsedMilliseconds);

            throw new NgbConfigurationViolationException("Operational registers core schema validation failed:\n- " + string.Join("\n- ", errors));
        }

        logger.LogInformation("Operational registers core schema validation OK in {ElapsedMs} ms.", sw.ElapsedMilliseconds);
    }
}
