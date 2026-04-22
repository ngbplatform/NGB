using System.Text;
using NGB.Persistence.Migrations;
using NGB.PostgreSql.Migrations.Internal;

namespace NGB.PostgreSql.Migrations.OperationalRegisters;

/// <summary>
/// Extra DB-level guards for Operational Registers.
/// </summary>
public sealed class OperationalRegisterExtraGuardsMigration : IDdlObject
{
    public string Name => "operational_registers_extra_guards";

    public string Generate()
    {
        var sql = new StringBuilder();

        sql.AppendLine("""
                       -- Guard: prevent dangerous registry mutations after movements exist.
                       CREATE OR REPLACE FUNCTION ngb_opreg_forbid_register_mutation_when_has_movements()
                       RETURNS trigger AS $$
                       BEGIN
                           IF COALESCE(OLD.has_movements, FALSE) THEN
                               IF TG_OP = 'DELETE' THEN
                                   RAISE EXCEPTION 'Operational register metadata is immutable after movements exist.';
                               END IF;

                               IF TG_OP = 'UPDATE' THEN
                                   -- code drives generated code_norm/table_code (and thus dynamic per-register table names)
                                   -- NOTE: compare only the base column. Generated columns may be recomputed after BEFORE triggers.
                                   IF NEW.register_id IS DISTINCT FROM OLD.register_id
                                      OR NEW.code IS DISTINCT FROM OLD.code
                                   THEN
                                       RAISE EXCEPTION 'Operational register code is immutable after movements exist.';
                                   END IF;

                                   IF OLD.has_movements = TRUE AND NEW.has_movements = FALSE THEN
                                       RAISE EXCEPTION 'Operational register has_movements can never flip back to FALSE.';
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
            triggerName: "trg_opreg_registers_immutable_when_has_movements",
            tableName: "operational_registers",
            triggerEvents: "BEFORE UPDATE OR DELETE",
            functionName: "ngb_opreg_forbid_register_mutation_when_has_movements"));

        sql.AppendLine("""

                       -- Guard: protect dimension rules from destructive changes after movements exist.
                       CREATE OR REPLACE FUNCTION ngb_opreg_forbid_dim_rule_mutation_when_has_movements()
                       RETURNS trigger AS $$
                       DECLARE
                           has_mov boolean;
                           reg_id uuid;
                       BEGIN
                           reg_id := COALESCE(NEW.register_id, OLD.register_id);

                           SELECT r.has_movements
                             INTO has_mov
                             FROM operational_registers r
                            WHERE r.register_id = reg_id;

                           IF COALESCE(has_mov, FALSE) THEN
                               IF TG_OP = 'DELETE' THEN
                                   RAISE EXCEPTION 'Operational register dimension rules are immutable after movements exist.';
                               END IF;

                               IF TG_OP = 'INSERT' THEN
                                   -- Adding an optional dimension rule is allowed for forward-only evolution,
                                   -- but adding a required rule would invalidate historical movements.
                                   IF COALESCE(NEW.is_required, FALSE) THEN
                                       RAISE EXCEPTION 'Cannot add a required dimension rule after movements exist.';
                                   END IF;
                               END IF;

                               IF TG_OP = 'UPDATE' THEN
                                   RAISE EXCEPTION 'Operational register dimension rules are append-only after movements exist.';
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
            triggerName: "trg_opreg_dim_rules_immutable_when_has_movements",
            tableName: "operational_register_dimension_rules",
            triggerEvents: "BEFORE INSERT OR UPDATE OR DELETE",
            functionName: "ngb_opreg_forbid_dim_rule_mutation_when_has_movements"));

        return sql.ToString();
    }
}
