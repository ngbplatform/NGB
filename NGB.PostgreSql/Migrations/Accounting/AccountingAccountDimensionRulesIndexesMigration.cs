using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Accounting;

/// <summary>
/// Indexes for <c>accounting_account_dimension_rules</c>.
/// </summary>
public sealed class AccountingAccountDimensionRulesIndexesMigration : IDdlObject
{
    public string Name => "accounting_account_dimension_rules__indexes";

    public string Generate() => """
                                CREATE UNIQUE INDEX IF NOT EXISTS ux_acc_dim_rules_account_ordinal
                                    ON accounting_account_dimension_rules(account_id, ordinal);

                                CREATE INDEX IF NOT EXISTS ix_acc_dim_rules_dimension_id
                                    ON accounting_account_dimension_rules(dimension_id);
                                """;
}
