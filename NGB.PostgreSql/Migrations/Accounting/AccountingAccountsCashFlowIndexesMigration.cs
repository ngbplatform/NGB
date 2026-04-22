using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Accounting;

public sealed class AccountingAccountsCashFlowIndexesMigration : IDdlObject
{
    public string Name => "accounting_accounts_cash_flow_indexes";

    public string Generate() => """
                                CREATE INDEX IF NOT EXISTS ix_acc_accounts_cash_flow_role
                                    ON accounting_accounts(cash_flow_role)
                                    WHERE is_deleted = FALSE AND cash_flow_role <> 0;

                                CREATE INDEX IF NOT EXISTS ix_acc_accounts_cash_flow_line_code
                                    ON accounting_accounts(cash_flow_line_code)
                                    WHERE is_deleted = FALSE AND cash_flow_line_code IS NOT NULL;
                                """;
}
