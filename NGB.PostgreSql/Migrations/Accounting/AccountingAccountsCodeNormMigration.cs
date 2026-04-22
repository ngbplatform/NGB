using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Accounting;

/// <summary>
/// Case-insensitive UX for Account.Code:
/// - Add a generated normalized column: code_norm = lower(btrim(code))
/// - Ensure trimming constraints on code/name (optional but recommended)
///
/// IMPORTANT:
/// - Uniqueness is enforced by an index on code_norm WHERE is_deleted = FALSE (created in indexes migration).
/// - Any existing duplicates by lower(trim(code)) will fail when the unique index is created.
/// </summary>
public sealed class AccountingAccountsCodeNormMigration : IDdlObject
{
    public string Name => "accounting_accounts_code_norm";

    public string Generate() => """
                                -- Add normalized code column (idempotent)
                                ALTER TABLE accounting_accounts
                                    ADD COLUMN IF NOT EXISTS code_norm TEXT GENERATED ALWAYS AS (lower(btrim(code))) STORED;

                                -- Optional hardening: enforce trimming (idempotent via DO blocks)
                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_acc_accounts_code_trimmed'
                                    ) THEN
                                        ALTER TABLE accounting_accounts
                                            ADD CONSTRAINT ck_acc_accounts_code_trimmed
                                            CHECK (code = btrim(code));
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_acc_accounts_name_trimmed'
                                    ) THEN
                                        ALTER TABLE accounting_accounts
                                            ADD CONSTRAINT ck_acc_accounts_name_trimmed
                                            CHECK (name = btrim(name));
                                    END IF;
                                END
                                $$;
                                """;
}
