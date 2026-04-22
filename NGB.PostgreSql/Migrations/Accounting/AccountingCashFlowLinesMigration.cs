using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Accounting;

public sealed class AccountingCashFlowLinesMigration : IDdlObject
{
    public string Name => "accounting_cash_flow_lines";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS accounting_cash_flow_lines (
                                    line_code TEXT PRIMARY KEY,
                                    method SMALLINT NOT NULL,
                                    section SMALLINT NOT NULL,
                                    label TEXT NOT NULL,
                                    sort_order INT NOT NULL,
                                    is_system BOOLEAN NOT NULL DEFAULT TRUE,
                                    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                                    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),

                                    CONSTRAINT ck_acc_cash_flow_lines_code_nonempty CHECK (length(trim(line_code)) > 0),
                                    CONSTRAINT ck_acc_cash_flow_lines_label_nonempty CHECK (length(trim(label)) > 0),
                                    CONSTRAINT ck_acc_cash_flow_lines_method_range CHECK (method BETWEEN 1 AND 1),
                                    CONSTRAINT ck_acc_cash_flow_lines_section_range CHECK (section BETWEEN 1 AND 4)
                                );

                                INSERT INTO accounting_cash_flow_lines(line_code, method, section, label, sort_order, is_system, created_at_utc, updated_at_utc)
                                VALUES
                                    ('op_wc_accounts_receivable', 1, 1, 'Change in Accounts Receivable', 110, TRUE, NOW(), NOW()),
                                    ('op_wc_accounts_payable', 1, 1, 'Change in Accounts Payable', 120, TRUE, NOW(), NOW()),
                                    ('op_wc_inventory', 1, 1, 'Change in Inventory', 130, TRUE, NOW(), NOW()),
                                    ('op_wc_prepaids', 1, 1, 'Change in Prepaid Expenses', 140, TRUE, NOW(), NOW()),
                                    ('op_wc_other_current_assets', 1, 1, 'Change in Other Current Assets', 150, TRUE, NOW(), NOW()),
                                    ('op_wc_accrued_liabilities', 1, 1, 'Change in Accrued Liabilities', 160, TRUE, NOW(), NOW()),
                                    ('op_wc_other_current_liabilities', 1, 1, 'Change in Other Current Liabilities', 170, TRUE, NOW(), NOW()),
                                    ('op_adjust_depreciation_amortization', 1, 1, 'Depreciation and amortization', 210, TRUE, NOW(), NOW()),
                                    ('op_adjust_noncash_gains_losses', 1, 1, 'Non-cash gains and losses', 220, TRUE, NOW(), NOW()),
                                    ('op_adjust_other_noncash', 1, 1, 'Other non-cash operating adjustments', 230, TRUE, NOW(), NOW()),
                                    ('inv_property_equipment_net', 1, 2, 'Property and equipment, net', 310, TRUE, NOW(), NOW()),
                                    ('inv_intangibles_net', 1, 2, 'Intangible assets, net', 320, TRUE, NOW(), NOW()),
                                    ('inv_investments_net', 1, 2, 'Investments, net', 330, TRUE, NOW(), NOW()),
                                    ('inv_loans_receivable_net', 1, 2, 'Loans receivable, net', 340, TRUE, NOW(), NOW()),
                                    ('inv_other_net', 1, 2, 'Other investing activities, net', 390, TRUE, NOW(), NOW()),
                                    ('fin_owner_equity_net', 1, 3, 'Owner equity transactions, net', 410, TRUE, NOW(), NOW()),
                                    ('fin_distributions_net', 1, 3, 'Owner distributions, net', 420, TRUE, NOW(), NOW()),
                                    ('fin_debt_net', 1, 3, 'Borrowings and repayments, net', 430, TRUE, NOW(), NOW()),
                                    ('fin_other_net', 1, 3, 'Other financing activities, net', 490, TRUE, NOW(), NOW())
                                ON CONFLICT (line_code) DO UPDATE
                                SET
                                    method = EXCLUDED.method,
                                    section = EXCLUDED.section,
                                    label = EXCLUDED.label,
                                    sort_order = EXCLUDED.sort_order,
                                    is_system = EXCLUDED.is_system,
                                    updated_at_utc = NOW();
                                """;
}
