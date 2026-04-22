using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Accounting;

/// <summary>
/// Chart of Accounts storage.
///
/// IMPORTANT:
/// - Accounts are soft-deletable (is_deleted).
/// - Posting/register tables reference accounts by UUID (account_id).
/// - Code is kept for human-readable identification and must be unique among non-deleted accounts.
/// </summary>
public sealed class AccountingAccountsMigration : IDdlObject
{
    public string Name => "accounting_accounts";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS accounting_accounts (
                                    account_id UUID PRIMARY KEY,

                                    code TEXT NOT NULL,
                                    name TEXT NOT NULL,
                                    account_type SMALLINT NOT NULL,
                                    statement_section SMALLINT NOT NULL,

                                    is_contra BOOLEAN NOT NULL DEFAULT FALSE,

                                    negative_balance_policy SMALLINT NOT NULL,

                                    is_active BOOLEAN NOT NULL DEFAULT TRUE,
                                    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,

                                    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                                    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),

                                    CONSTRAINT ck_acc_accounts_code_nonempty CHECK (length(trim(code)) > 0),
                                    CONSTRAINT ck_acc_accounts_name_nonempty CHECK (length(trim(name)) > 0),
                                    CONSTRAINT ck_acc_accounts_statement_section_range CHECK (statement_section BETWEEN 1 AND 8)
                                );
                                """;
}
