using System.Text;
using NGB.Persistence.Migrations;
using NGB.PostgreSql.Migrations.Internal;

namespace NGB.PostgreSql.Migrations.ReferenceRegisters;

/// <summary>
/// Extra DB-level guards for Reference Registers.
///
/// These guards protect production invariants even if callers bypass runtime services:
/// - once <c>has_records</c> is TRUE, register code/periodicity/record_mode become immutable
/// - <c>has_records</c> is monotonic (can never flip back to FALSE)
/// - once <c>has_records</c> is TRUE, dimension rules become append-only; adding required rules is forbidden
/// </summary>
public sealed class ReferenceRegisterExtraGuardsMigration : IDdlObject
{
    public string Name => "reference_registers_extra_guards";

    public string Generate()
    {
        var sql = new StringBuilder();

        sql.AppendLine("""
                       -- Guard: prevent dangerous registry mutations after records exist.
                       CREATE OR REPLACE FUNCTION ngb_refreg_forbid_register_mutation_when_has_records()
                       RETURNS trigger AS $$
                       BEGIN
                           IF COALESCE(OLD.has_records, FALSE) THEN
                               IF TG_OP = 'DELETE' THEN
                                   RAISE EXCEPTION 'Reference register metadata is immutable after records exist.';
                               END IF;

                               IF TG_OP = 'UPDATE' THEN
                                   -- code drives generated code_norm/table_code (and thus dynamic per-register table names)
                                   IF NEW.register_id IS DISTINCT FROM OLD.register_id
                                      OR NEW.code IS DISTINCT FROM OLD.code
                                      OR NEW.periodicity IS DISTINCT FROM OLD.periodicity
                                      OR NEW.record_mode IS DISTINCT FROM OLD.record_mode
                                   THEN
                                       RAISE EXCEPTION 'Reference register code/periodicity/record_mode are immutable after records exist.';
                                   END IF;

                                   IF OLD.has_records = TRUE AND NEW.has_records = FALSE THEN
                                       RAISE EXCEPTION 'Reference register has_records can never flip back to FALSE.';
                                   END IF;
                               END IF;
                           END IF;

                           IF TG_OP = 'DELETE' THEN
                               RETURN OLD;
                           END IF;

                           RETURN NEW;
                       END;
                       $$ LANGUAGE plpgsql;
                       """);

        sql.AppendLine(PostgresMigrationTriggerSql.CreateTriggerIfNotExists(
            triggerName: "trg_refreg_registers_immutable_when_has_records",
            tableName: "reference_registers",
            triggerEvents: "BEFORE UPDATE OR DELETE",
            functionName: "ngb_refreg_forbid_register_mutation_when_has_records"));

        sql.AppendLine("""

                       -- Guard: protect dimension rules from destructive changes after records exist.
                       CREATE OR REPLACE FUNCTION ngb_refreg_forbid_dim_rule_mutation_when_has_records()
                       RETURNS trigger AS $$
                       DECLARE
                           has_rec boolean;
                           reg_id uuid;
                       BEGIN
                           reg_id := COALESCE(NEW.register_id, OLD.register_id);

                           SELECT r.has_records
                             INTO has_rec
                             FROM reference_registers r
                            WHERE r.register_id = reg_id;

                           IF COALESCE(has_rec, FALSE) THEN
                               IF TG_OP = 'DELETE' THEN
                                   RAISE EXCEPTION 'Reference register dimension rules are immutable after records exist.';
                               END IF;

                               IF TG_OP = 'INSERT' THEN
                                   -- Adding an optional dimension rule is allowed for forward-only evolution,
                                   -- but adding a required rule would invalidate historical records.
                                   IF COALESCE(NEW.is_required, FALSE) THEN
                                       RAISE EXCEPTION 'Cannot add a required dimension rule after records exist.';
                                   END IF;
                               END IF;

                               IF TG_OP = 'UPDATE' THEN
                                   RAISE EXCEPTION 'Reference register dimension rules are append-only after records exist.';
                               END IF;
                           END IF;

                           IF TG_OP = 'DELETE' THEN
                               RETURN OLD;
                           END IF;

                           RETURN NEW;
                       END;
                       $$ LANGUAGE plpgsql;
                       """);

        sql.AppendLine(PostgresMigrationTriggerSql.CreateTriggerIfNotExists(
            triggerName: "trg_refreg_dim_rules_immutable_when_has_records",
            tableName: "reference_register_dimension_rules",
            triggerEvents: "BEFORE INSERT OR UPDATE OR DELETE",
            functionName: "ngb_refreg_forbid_dim_rule_mutation_when_has_records"));

        return sql.ToString();
    }
}
