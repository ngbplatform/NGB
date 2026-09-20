using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.OperationalRegisters;

/// <summary>
/// Indexes for operational registers tables.
/// </summary>
public sealed class OperationalRegistersIndexesMigration : IDdlObject
{
    public string Name => "operational_registers_indexes";

    public string Generate() => """
                                -- registry
                                CREATE UNIQUE INDEX IF NOT EXISTS ux_operational_registers_code_norm
                                    ON operational_registers(code_norm);

                                -- physical per-register tables are derived from table_code (see OperationalRegisterNaming)
                                CREATE UNIQUE INDEX IF NOT EXISTS ux_operational_registers_table_code
                                    ON operational_registers(table_code);

                                -- resources (metadata -> physical column schema)
                                CREATE INDEX IF NOT EXISTS ix_opreg_resources_register_ordinal
                                    ON operational_register_resources(register_id, ordinal, code_norm);

                                -- dimension rules
                                CREATE INDEX IF NOT EXISTS ix_opreg_dim_rules_register_ordinal
                                    ON operational_register_dimension_rules(register_id, ordinal);

                                -- finalizations
                                CREATE INDEX IF NOT EXISTS ix_opreg_finalizations_register_period
                                    ON operational_register_finalizations(register_id, period);

                                -- write log
                                CREATE INDEX IF NOT EXISTS ix_opreg_write_log_document
                                    ON operational_register_write_state(document_id);
                                """;
}
