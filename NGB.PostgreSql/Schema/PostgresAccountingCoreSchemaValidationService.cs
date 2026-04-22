using System.Diagnostics;
using Dapper;
using Microsoft.Extensions.Logging;
using NGB.Persistence.Schema;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Schema.Internal;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Schema;

/// <summary>
/// PostgreSQL validator for the accounting core schema.
///
/// This validator is intentionally conservative: it fails fast when the platform
/// can't safely run (correctness + performance invariants).
/// </summary>
public sealed class PostgresAccountingCoreSchemaValidationService(
    IDbSchemaInspector schemaInspector,
    IUnitOfWork uow,
    ILogger<PostgresAccountingCoreSchemaValidationService> logger)
    : IAccountingCoreSchemaValidationService
{
    private static readonly Guid EmptyDimensionSetId = Guid.Empty;

    public async Task ValidateAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var snapshot = await schemaInspector.GetSnapshotAsync(ct);
        var errors = new List<string>();

        // 1) Core tables (accounting + platform dimensions/dimension sets)
        PostgresSchemaValidationChecks.RequireTable(snapshot, "accounting_accounts", errors);
        PostgresSchemaValidationChecks.RequireTable(snapshot, "accounting_account_dimension_rules", errors);
        PostgresSchemaValidationChecks.RequireTable(snapshot, "platform_dimensions", errors);
        PostgresSchemaValidationChecks.RequireTable(snapshot, "platform_dimension_sets", errors);
        PostgresSchemaValidationChecks.RequireTable(snapshot, "platform_dimension_set_items", errors);
        PostgresSchemaValidationChecks.RequireTable(snapshot, "accounting_register_main", errors);
        PostgresSchemaValidationChecks.RequireTable(snapshot, "accounting_turnovers", errors);
        PostgresSchemaValidationChecks.RequireTable(snapshot, "accounting_balances", errors);
        PostgresSchemaValidationChecks.RequireTable(snapshot, "accounting_closed_periods", errors);
        PostgresSchemaValidationChecks.RequireTable(snapshot, "accounting_posting_state", errors);

        // Documents registry (required by posting + document workflows)
        PostgresSchemaValidationChecks.RequireTable(snapshot, "documents", errors);
        PostgresSchemaValidationChecks.RequireTable(snapshot, "document_number_sequences", errors);

        // 2) Minimal column contracts
        PostgresSchemaValidationChecks.RequireColumns(
            snapshot,
            tableName: "accounting_register_main",
            required:
            [
                "entry_id",
                "document_id",
                "period",
                "period_month",
                "debit_account_id",
                "credit_account_id",
                "debit_dimension_set_id",
                "credit_dimension_set_id",
                "amount",
                "is_storno"
            ],
            errors);

        PostgresSchemaValidationChecks.RequireColumns(
            snapshot,
            tableName: "platform_dimensions",
            required:
            [
                "dimension_id",
                "code",
                "code_norm",
                "name",
                "is_active",
                "is_deleted"
            ],
            errors);

        // 3) Critical indexes
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "accounting_register_main", "ix_acc_reg_period_month", errors);
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "accounting_register_main", "ix_acc_reg_document_id", errors);

        // DimensionSet-aware month slicing (after enabling Dimension Sets)
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "accounting_register_main", "ix_acc_reg_debit_month_dimset", errors);
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "accounting_register_main", "ix_acc_reg_credit_month_dimset", errors);

        // Dimension rules determinism + lookup
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "accounting_account_dimension_rules", "ux_acc_dim_rules_account_ordinal", errors);
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "accounting_account_dimension_rules", "ix_acc_dim_rules_dimension_id", errors);

        // Dimension set items
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "platform_dimension_set_items", "ix_platform_dimset_items_set", errors);
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "platform_dimension_set_items", "ix_platform_dimset_items_dimension_value_set", errors);

        // Platform dimension code uniqueness (code_norm, not deleted)
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "platform_dimensions", "ux_platform_dimensions_code_norm_not_deleted", errors);

        // Document numbering uniqueness (type-scoped)
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "documents", "ux_documents_type_number_not_null", errors);

        // 4) Critical foreign keys (dimension sets)
        PostgresSchemaValidationChecks.RequireForeignKey(snapshot, "accounting_register_main", "debit_dimension_set_id", "platform_dimension_sets", "dimension_set_id", errors);
        PostgresSchemaValidationChecks.RequireForeignKey(snapshot, "accounting_register_main", "credit_dimension_set_id", "platform_dimension_sets", "dimension_set_id", errors);

        // 5) DB-level guards and invariants
        await uow.EnsureConnectionOpenAsync(ct);

        // 5.1) Closed-period guards (defense in depth)
        await PostgresSchemaValidationChecks.RequireFunctionAsync(uow, "ngb_forbid_posting_into_closed_period", errors, ct);
        await PostgresSchemaValidationChecks.RequireTriggerAsync(uow, "trg_acc_reg_no_closed_period", "accounting_register_main", errors, ct);
        await PostgresSchemaValidationChecks.RequireTriggerAsync(uow, "trg_acc_reg_no_closed_period_delete", "accounting_register_main", errors, ct);
        await PostgresSchemaValidationChecks.RequireTriggerAsync(uow, "trg_acc_turnovers_no_closed_period", "accounting_turnovers", errors, ct);
        await PostgresSchemaValidationChecks.RequireTriggerAsync(uow, "trg_acc_turnovers_no_closed_period_delete", "accounting_turnovers", errors, ct);
        await PostgresSchemaValidationChecks.RequireTriggerAsync(uow, "trg_acc_balances_no_closed_period", "accounting_balances", errors, ct);
        await PostgresSchemaValidationChecks.RequireTriggerAsync(uow, "trg_acc_balances_no_closed_period_delete", "accounting_balances", errors, ct);

        // 5.2) Typed posted-document immutability guards
        await PostgresSchemaValidationChecks.RequireFunctionAsync(uow, "ngb_forbid_mutation_of_posted_document", errors, ct);
        await PostgresSchemaValidationChecks.RequireFunctionAsync(uow, "ngb_install_typed_document_immutability_guards", errors, ct);

        // Every typed document table is a public table starting with 'doc_' and containing 'document_id'.
        // Such tables must be protected by reusable trigger 'trg_posted_immutable'.
        var missingDocTables = (await uow.Connection.QueryAsync<string>(
            new CommandDefinition(
                """
                SELECT c.table_name
                FROM information_schema.columns c
                WHERE c.table_schema = 'public'
                  AND c.column_name = 'document_id'
                  AND c.table_name LIKE E'doc\\_%' ESCAPE E'\\'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM pg_trigger t
                      JOIN pg_class cl ON cl.oid = t.tgrelid
                      JOIN pg_namespace ns ON ns.oid = cl.relnamespace
                      WHERE t.tgname = 'trg_posted_immutable'
                        AND NOT t.tgisinternal
                        AND ns.nspname = c.table_schema
                        AND cl.relname = c.table_name
                  )
                ORDER BY c.table_name;
                """,
                transaction: uow.Transaction,
                cancellationToken: ct))).ToArray();

        foreach (var table in missingDocTables)
        {
            errors.Add($"Missing trigger 'trg_posted_immutable' on typed document table '{table}'.");
        }

        // 5.3) Dimension Sets must be immutable snapshots (append-only)
        await PostgresSchemaValidationChecks.RequireFunctionAsync(uow, "ngb_forbid_mutation_of_append_only_table", errors, ct);
        await PostgresSchemaValidationChecks.RequireTriggerAsync(uow, "trg_platform_dimension_sets_append_only", "platform_dimension_sets", errors, ct);
        await PostgresSchemaValidationChecks.RequireTriggerAsync(uow, "trg_platform_dimension_set_items_append_only", "platform_dimension_set_items", errors, ct);

        // 5.4) Reserved empty DimensionSet (Guid.Empty) must exist and must be empty (no items)
        var emptySetRowExists = await uow.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM platform_dimension_sets WHERE dimension_set_id = @id;",
                new { id = EmptyDimensionSetId },
                transaction: uow.Transaction,
                cancellationToken: ct));

        if (emptySetRowExists == 0)
            errors.Add($"Missing reserved empty dimension set row in platform_dimension_sets for id '{EmptyDimensionSetId}'.");

        var emptySetItemCount = await uow.Connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM platform_dimension_set_items WHERE dimension_set_id = @id;",
                new { id = EmptyDimensionSetId },
                transaction: uow.Transaction,
                cancellationToken: ct));

        if (emptySetItemCount != 0)
            errors.Add($"Reserved empty-set row (Guid.Empty) must have zero items in platform_dimension_set_items, but found {emptySetItemCount} row(s).");

        if (errors.Count > 0)
        {
            logger.LogError(
                "Accounting core schema validation FAILED with {ErrorCount} errors in {ElapsedMs} ms.",
                errors.Count,
                sw.ElapsedMilliseconds);

            throw new NgbConfigurationViolationException("Accounting core schema validation failed:\n- " + string.Join("\n- ", errors));
        }

        logger.LogInformation("Accounting core schema validation OK in {ElapsedMs} ms.", sw.ElapsedMilliseconds);
    }
}
