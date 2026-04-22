using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.ReferenceRegisters;

/// <summary>
/// Indexes for Reference Registers metadata tables.
/// </summary>
public sealed class ReferenceRegistersIndexesMigration : IDdlObject
{
    public string Name => "reference_registers_indexes";

    public string Generate() => """
                                -- registry
                                CREATE UNIQUE INDEX IF NOT EXISTS ux_reference_registers_code_norm
                                    ON reference_registers(code_norm);

                                -- physical per-register tables are derived from table_code
                                CREATE UNIQUE INDEX IF NOT EXISTS ux_reference_registers_table_code
                                    ON reference_registers(table_code);

                                -- fields (metadata -> physical column schema)
                                CREATE INDEX IF NOT EXISTS ix_refreg_fields_register_ordinal
                                    ON reference_register_fields(register_id, ordinal, code_norm);

                                -- key dimension rules
                                CREATE INDEX IF NOT EXISTS ix_refreg_dim_rules_register_ordinal
                                    ON reference_register_dimension_rules(register_id, ordinal);

                                -- idempotency log
                                CREATE INDEX IF NOT EXISTS ix_refreg_write_log_document
                                    ON reference_register_write_state(document_id);
                                """;
}
