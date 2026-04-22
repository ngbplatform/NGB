using System.Text.Json;
using Dapper;
using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Performance;

[Collection(PostgresCollection.Name)]
public sealed class ExplainPlans_IndexUsage_P4_2_Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task TurnoversRangeQuery_AvoidsSeqScan_WhenSeqScanDisabled()
    {
        // Arrange: seed minimal CoA so FK allows inserts.
        var accountId = await EnsureAccountAsync(Fixture.ConnectionString, code: "it_1000", name: "IT Cash",
            type: AccountType.Asset, section: StatementSection.Assets);

        await SeedTurnoversAsync(Fixture.ConnectionString, accountId);

        // Act
        var json = await ExplainJsonAsync(
            Fixture.ConnectionString,
            """
            SELECT period, account_id, dimension_set_id, debit_amount, credit_amount
            FROM accounting_turnovers
            WHERE period >= @from AND period <= @to
            ORDER BY period, account_id, dimension_set_id
            """,
            new
            {
                from = new DateOnly(2025, 1, 1),
                to = new DateOnly(2026, 12, 1)
            },
            disableSeqScan: true);

        // Assert: no sequential scan on turnovers.
        PlanContainsNodeTypeOnRelation(json, "Seq Scan", "accounting_turnovers").Should().BeFalse();

        // Any index-backed plan is fine (planner may choose PK or one of the supporting indexes).
        (PlanContainsIndex(json, "accounting_turnovers_pkey")
         || PlanContainsIndex(json, "ix_acc_turnovers_period_account")
         || PlanContainsIndex(json, "ix_acc_turnovers_account_period")
         || PlanContainsIndex(json, "ix_turnovers_period_account")
         || PlanContainsIndex(json, "ix_turnovers_account_period"))
            .Should().BeTrue("period range queries must be index-backed");
    }

    [Fact]
    public async Task GeneralJournalPageQuery_UsesDedicatedPageOrderIndex_WhenSeqScanDisabled()
    {
        var (debitId, creditId) = await SeedRegisterRowsAsync(Fixture.ConnectionString, rows: 6_000);

        var json = await ExplainJsonAsync(
            Fixture.ConnectionString,
            """
            SELECT r.entry_id
            FROM accounting_register_main r
            WHERE r.period >= @from_utc
              AND r.period < @to_exclusive_utc
            ORDER BY r.period, r.entry_id
            LIMIT 51
            """,
            new
            {
                from_utc = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                to_exclusive_utc = new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            disableSeqScan: true);

        PlanContainsNodeTypeOnRelation(json, "Seq Scan", "accounting_register_main").Should().BeFalse();

        PlanContainsIndex(json, "ix_acc_reg_general_journal_page_order")
            .Should().BeTrue("general journal paging must use the dedicated (period, entry_id) index");

        debitId.Should().NotBe(Guid.Empty);
        creditId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task GeneralJournalPageQuery_WithDocumentFilter_UsesDocumentPageOrderIndex_WhenSeqScanDisabled()
    {
        await SeedRegisterRowsAsync(Fixture.ConnectionString, rows: 6_000);
        var documentId = GuidUtility.DeterministicGuid("doc", "17");

        var json = await ExplainJsonAsync(
            Fixture.ConnectionString,
            """
            SELECT r.entry_id
            FROM accounting_register_main r
            WHERE r.document_id = @doc
              AND r.period >= @from_utc
              AND r.period < @to_exclusive_utc
            ORDER BY r.period, r.entry_id
            LIMIT 51
            """,
            new
            {
                doc = documentId,
                from_utc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                to_exclusive_utc = new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            disableSeqScan: true);

        PlanContainsNodeTypeOnRelation(json, "Seq Scan", "accounting_register_main").Should().BeFalse();
        PlanContainsIndex(json, "ix_acc_reg_general_journal_document_page_order")
            .Should().BeTrue("document-filtered general journal paging must use the dedicated document/page-order index");
    }

    [Fact]
    public async Task GeneralJournalPageQuery_WithDebitAccountFilter_UsesDebitPageOrderIndex_WhenSeqScanDisabled()
    {
        var (debitId, _) = await SeedRegisterRowsAsync(Fixture.ConnectionString, rows: 6_000);

        var json = await ExplainJsonAsync(
            Fixture.ConnectionString,
            """
            SELECT r.entry_id
            FROM accounting_register_main r
            WHERE r.debit_account_id = @acc
              AND r.period >= @from_utc
              AND r.period < @to_exclusive_utc
            ORDER BY r.period, r.entry_id
            LIMIT 51
            """,
            new
            {
                acc = debitId,
                from_utc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                to_exclusive_utc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            disableSeqScan: true);

        PlanContainsNodeTypeOnRelation(json, "Seq Scan", "accounting_register_main").Should().BeFalse();
        PlanContainsIndex(json, "ix_acc_reg_general_journal_debit_page_order")
            .Should().BeTrue("debit-filtered general journal paging must use the dedicated debit/page-order index");
    }

    [Fact]
    public async Task LedgerAnalysisFlatDetailCursorQuery_UsesGeneralJournalPageOrderIndex_WhenSeqScanDisabled()
    {
        await SeedRegisterRowsAsync(Fixture.ConnectionString, rows: 6_000);

        var json = await ExplainJsonAsync(
            Fixture.ConnectionString,
            """
            SELECT x.entry_id, x.posting_side
            FROM (
                SELECT r.period, r.entry_id, 'debit'::text AS posting_side
                FROM accounting_register_main r

                UNION ALL

                SELECT r.period, r.entry_id, 'credit'::text AS posting_side
                FROM accounting_register_main r
            ) x
            WHERE x.period >= @from_utc
              AND x.period < @to_exclusive_utc
            ORDER BY x.period, x.entry_id, x.posting_side
            LIMIT 51
            """,
            new
            {
                from_utc = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                to_exclusive_utc = new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            disableSeqScan: true);

        PlanContainsNodeTypeOnRelation(json, "Seq Scan", "accounting_register_main").Should().BeFalse();
        PlanContainsIndex(json, "ix_acc_reg_general_journal_page_order")
            .Should().BeTrue("ledger analysis flat-detail cursor paging should stay index-backed by the shared (period, entry_id) page-order index");
    }

    [Fact]
    public async Task AccountCardEffectivePageQuery_UsesAccountPageOrderIndexes_WhenSeqScanDisabled()
    {
        var (debitId, creditId) = await SeedRegisterRowsAsync(Fixture.ConnectionString, rows: 6_000);

        var debitJson = await ExplainJsonAsync(
            Fixture.ConnectionString,
            """
            SELECT r.entry_id
            FROM accounting_register_main r
            WHERE r.debit_account_id = @acc
              AND r.period >= @from_utc
              AND r.period < @to_exclusive_utc
            ORDER BY r.period, r.entry_id
            LIMIT 51
            """,
            new
            {
                from_utc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                to_exclusive_utc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                acc = debitId
            },
            disableSeqScan: true);

        PlanContainsNodeTypeOnRelation(debitJson, "Seq Scan", "accounting_register_main").Should().BeFalse();

        (PlanContainsIndex(debitJson, "ix_acc_reg_account_card_debit_dim_page_order")
         || PlanContainsIndex(debitJson, "ix_acc_reg_general_journal_debit_page_order"))
            .Should().BeTrue("account card effective debit-side paging must be index-backed by a debit account/page-order index");

        var creditJson = await ExplainJsonAsync(
            Fixture.ConnectionString,
            """
            SELECT r.entry_id
            FROM accounting_register_main r
            WHERE r.credit_account_id = @acc
              AND r.period >= @from_utc
              AND r.period < @to_exclusive_utc
            ORDER BY r.period, r.entry_id
            LIMIT 51
            """,
            new
            {
                from_utc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                to_exclusive_utc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                acc = creditId
            },
            disableSeqScan: true);

        PlanContainsNodeTypeOnRelation(creditJson, "Seq Scan", "accounting_register_main").Should().BeFalse();

        (PlanContainsIndex(creditJson, "ix_acc_reg_account_card_credit_dim_page_order")
         || PlanContainsIndex(creditJson, "ix_acc_reg_general_journal_credit_page_order"))
            .Should().BeTrue("account card effective credit-side paging must be index-backed by a credit account/page-order index");
    }

    [Fact]
    public async Task CashFlowIndirect_DebitCashSlice_UsesDedicatedCashFlowDebitIndex_WhenSeqScanDisabled()
    {
        var (cashDebitId, _) = await SeedRegisterRowsAsync(Fixture.ConnectionString, rows: 6_000);

        var json = await ExplainJsonAsync(
            Fixture.ConnectionString,
            """
            SELECT r.credit_account_id
            FROM accounting_register_main r
            WHERE r.debit_account_id = @cash_account_id
              AND r.period >= @from_utc
              AND r.period < @to_exclusive_utc
            ORDER BY r.period, r.credit_account_id
            LIMIT 51
            """,
            new
            {
                cash_account_id = cashDebitId,
                from_utc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                to_exclusive_utc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            disableSeqScan: true);

        PlanContainsNodeTypeOnRelation(json, "Seq Scan", "accounting_register_main").Should().BeFalse();
        PlanContainsIndex(json, "ix_acc_reg_cash_flow_debit_cash_period_counter")
            .Should().BeTrue("debit cash-side cash-flow slices must use the dedicated (debit_account_id, period, credit_account_id) index");
    }

    [Fact]
    public async Task CashFlowIndirect_CreditCashSlice_UsesDedicatedCashFlowCreditIndex_WhenSeqScanDisabled()
    {
        var (_, cashCreditId) = await SeedRegisterRowsAsync(Fixture.ConnectionString, rows: 6_000);

        var json = await ExplainJsonAsync(
            Fixture.ConnectionString,
            """
            SELECT r.debit_account_id
            FROM accounting_register_main r
            WHERE r.credit_account_id = @cash_account_id
              AND r.period >= @from_utc
              AND r.period < @to_exclusive_utc
            ORDER BY r.period, r.debit_account_id
            LIMIT 51
            """,
            new
            {
                cash_account_id = cashCreditId,
                from_utc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                to_exclusive_utc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            disableSeqScan: true);

        PlanContainsNodeTypeOnRelation(json, "Seq Scan", "accounting_register_main").Should().BeFalse();
        PlanContainsIndex(json, "ix_acc_reg_cash_flow_credit_cash_period_counter")
            .Should().BeTrue("credit cash-side cash-flow slices must use the dedicated (credit_account_id, period, debit_account_id) index");
    }

    [Fact]
    public async Task GeneralLedgerAggregatedDebitSlice_UsesDedicatedGroupingIndex_WhenSeqScanDisabled()
    {
        var (debitId, _) = await SeedGeneralLedgerAggregatedRowsAsync(Fixture.ConnectionString, rowsPerSide: 12_000);

        var json = await ExplainJsonAsync(
            Fixture.ConnectionString,
            """
            SELECT r.document_id, r.credit_account_id, r.debit_dimension_set_id
            FROM accounting_register_main r
            WHERE r.debit_account_id = @acc
              AND r.period >= @from_utc
              AND r.period < @to_exclusive_utc
            ORDER BY r.period, r.document_id, r.credit_account_id, r.debit_dimension_set_id
            LIMIT 51
            """,
            new
            {
                from_utc = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                to_exclusive_utc = new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                acc = debitId
            },
            disableSeqScan: true);

        PlanContainsNodeTypeOnRelation(json, "Seq Scan", "accounting_register_main").Should().BeFalse();

        PlanContainsIndex(json, "ix_acc_reg_gl_agg_debit_group_page_order")
            .Should().BeTrue("GL aggregated debit-side grouping and paging should use the dedicated composite index");
    }

    [Fact]
    public async Task GeneralLedgerAggregatedCreditSlice_UsesDedicatedGroupingIndex_WhenSeqScanDisabled()
    {
        var (_, creditId) = await SeedGeneralLedgerAggregatedRowsAsync(Fixture.ConnectionString, rowsPerSide: 12_000);

        var json = await ExplainJsonAsync(
            Fixture.ConnectionString,
            """
            SELECT r.document_id, r.debit_account_id, r.credit_dimension_set_id
            FROM accounting_register_main r
            WHERE r.credit_account_id = @acc
              AND r.period >= @from_utc
              AND r.period < @to_exclusive_utc
            ORDER BY r.period, r.document_id, r.debit_account_id, r.credit_dimension_set_id
            LIMIT 51
            """,
            new
            {
                from_utc = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                to_exclusive_utc = new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                acc = creditId
            },
            disableSeqScan: true);

        PlanContainsNodeTypeOnRelation(json, "Seq Scan", "accounting_register_main").Should().BeFalse();

        PlanContainsIndex(json, "ix_acc_reg_gl_agg_credit_group_page_order")
            .Should().BeTrue("GL aggregated credit-side grouping and paging should use the dedicated composite index");
    }

    private static async Task<Guid> EnsureAccountAsync(
        string cs,
        string code,
        string name,
        AccountType type,
        StatementSection section)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        var existing = await conn.ExecuteScalarAsync<Guid?>(
            """
            SELECT account_id
            FROM accounting_accounts
            WHERE code = @code AND is_deleted = false
            LIMIT 1;
            """,
            new { code });

        if (existing is not null)
            return existing.Value;

        var id = Guid.CreateVersion7();
        await conn.ExecuteAsync(
            """
            INSERT INTO accounting_accounts(
                account_id, code, name, account_type, statement_section,
                is_contra,
                negative_balance_policy,
                is_active, is_deleted,
                created_at_utc, updated_at_utc)
            VALUES(
                @id, @code, @name, @type, @section,
                false,
                0,
                TRUE, FALSE,
                NOW(), NOW()
            );
            """,
            new
            {
                id,
                code,
                name,
                type = (short)type,
                section = (short)section
            });

        return id;
    }

    private static async Task SeedTurnoversAsync(string cs, Guid accountId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        // Keep it small: we only need a non-empty table + ANALYZE for planner stats.
        for (int i = 0; i < 200; i++)
        {
            var period = new DateOnly(2025, 1, 1).AddMonths(i % 24);
            await conn.ExecuteAsync(
                """
                INSERT INTO accounting_turnovers(
                    period, account_id, dimension_set_id,
                    debit_amount, credit_amount)
                VALUES(
                    @period, @account_id, @set_id,
                    1, 0
                )
                ON CONFLICT DO NOTHING;
                """,
                new
                {
                    period = period,
                    account_id = accountId,
                    set_id = Guid.Empty
                });
        }

        await conn.ExecuteAsync("ANALYZE accounting_turnovers;");
    }

    private static async Task<(Guid cashId, Guid revenueId)> SeedRegisterRowsAsync(string cs, int rows)
    {
        var cashId = await EnsureAccountAsync(cs, "it_cash", "IT Cash", AccountType.Asset, StatementSection.Assets);
        var revenueId = await EnsureAccountAsync(cs, "it_rev", "IT Revenue", AccountType.Income, StatementSection.Income);
        var noiseCash1 = await EnsureAccountAsync(cs, "it_cash_noise_1", "IT Cash Noise 1", AccountType.Asset, StatementSection.Assets);
        var noiseRevenue1 = await EnsureAccountAsync(cs, "it_rev_noise_1", "IT Revenue Noise 1", AccountType.Income, StatementSection.Income);
        var noiseCash2 = await EnsureAccountAsync(cs, "it_cash_noise_2", "IT Cash Noise 2", AccountType.Asset, StatementSection.Assets);
        var noiseRevenue2 = await EnsureAccountAsync(cs, "it_rev_noise_2", "IT Revenue Noise 2", AccountType.Income, StatementSection.Income);

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        // Seed enough rows across MANY months to make month slicing selective.
        for (int i = 0; i < rows; i++)
        {
            // Spread across 24 months (2024-01..2025-12).
            var m = i % 24;
            var year = 2024 + (m / 12);
            var month = (m % 12) + 1;
            var day = (i % 28) + 1;
            var periodUtc = new DateTime(year, month, day, 12, 0, 0, DateTimeKind.Utc);
            var usesPrimaryPair = i % 6 == 0;
            var usesFirstNoisePair = !usesPrimaryPair && i % 2 == 0;
            var debitAccountId = usesPrimaryPair
                ? cashId
                : usesFirstNoisePair
                    ? noiseCash1
                    : noiseCash2;
            var creditAccountId = usesPrimaryPair
                ? revenueId
                : usesFirstNoisePair
                    ? noiseRevenue1
                    : noiseRevenue2;
            await conn.ExecuteAsync(
                """
                INSERT INTO accounting_register_main(
                    document_id, period,
                    debit_account_id, credit_account_id,
                    debit_dimension_set_id, credit_dimension_set_id,
                    amount, is_storno)
                VALUES(
                    @doc, @period,
                    @debit, @credit,
                    @empty_set, @empty_set,
                    1, false
                );
                """,
                new
                {
                    doc = GuidUtility.DeterministicGuid("doc", i.ToString()),
                    period = periodUtc,
                    debit = debitAccountId,
                    credit = creditAccountId,
                    empty_set = Guid.Empty
                });
        }

        await conn.ExecuteAsync("ANALYZE accounting_register_main;");

        return (cashId, revenueId);
    }

    private static async Task<(Guid debitTargetId, Guid creditTargetId)> SeedGeneralLedgerAggregatedRowsAsync(string cs, int rowsPerSide)
    {
        var debitTargetId = await EnsureAccountAsync(cs, "it_glagg_debit", "IT GL Aggregated Debit", AccountType.Asset, StatementSection.Assets);
        var creditTargetId = await EnsureAccountAsync(cs, "it_glagg_credit", "IT GL Aggregated Credit", AccountType.Income, StatementSection.Income);

        var debitCounterparties = new[]
        {
            await EnsureAccountAsync(cs, "it_glagg_credit_1", "IT GL Credit 1", AccountType.Income, StatementSection.Income),
            await EnsureAccountAsync(cs, "it_glagg_credit_2", "IT GL Credit 2", AccountType.Income, StatementSection.Income),
            await EnsureAccountAsync(cs, "it_glagg_credit_3", "IT GL Credit 3", AccountType.Income, StatementSection.Income),
            await EnsureAccountAsync(cs, "it_glagg_credit_4", "IT GL Credit 4", AccountType.Income, StatementSection.Income)
        };

        var creditCounterparties = new[]
        {
            await EnsureAccountAsync(cs, "it_glagg_debit_1", "IT GL Debit 1", AccountType.Asset, StatementSection.Assets),
            await EnsureAccountAsync(cs, "it_glagg_debit_2", "IT GL Debit 2", AccountType.Asset, StatementSection.Assets),
            await EnsureAccountAsync(cs, "it_glagg_debit_3", "IT GL Debit 3", AccountType.Asset, StatementSection.Assets),
            await EnsureAccountAsync(cs, "it_glagg_debit_4", "IT GL Debit 4", AccountType.Asset, StatementSection.Assets)
        };

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        for (int i = 0; i < rowsPerSide; i++)
        {
            var day = (i % 28) + 1;
            var minute = i % 60;
            var second = i % 60;
            var periodUtc = new DateTime(2025, 6, day, 12, minute, second, DateTimeKind.Utc);
            var creditCounterparty = debitCounterparties[i % debitCounterparties.Length];
            await conn.ExecuteAsync(
                """
                INSERT INTO accounting_register_main(
                    document_id, period,
                    debit_account_id, credit_account_id,
                    debit_dimension_set_id, credit_dimension_set_id,
                    amount, is_storno)
                VALUES(
                    @doc, @period,
                    @debit, @credit,
                    @debit_dim, @credit_dim,
                    1, false
                );
                """,
                new
                {
                    doc = GuidUtility.DeterministicGuid("glagg-debit-doc", i.ToString()),
                    period = periodUtc,
                    debit = debitTargetId,
                    credit = creditCounterparty,
                    debit_dim = Guid.Empty,
                    credit_dim = Guid.Empty
                });
        }

        for (int i = 0; i < rowsPerSide; i++)
        {
            var day = (i % 28) + 1;
            var minute = i % 60;
            var second = (i * 7) % 60;
            var periodUtc = new DateTime(2025, 6, day, 13, minute, second, DateTimeKind.Utc);
            var debitCounterparty = creditCounterparties[i % creditCounterparties.Length];
            await conn.ExecuteAsync(
                """
                INSERT INTO accounting_register_main(
                    document_id, period,
                    debit_account_id, credit_account_id,
                    debit_dimension_set_id, credit_dimension_set_id,
                    amount, is_storno)
                VALUES(
                    @doc, @period,
                    @debit, @credit,
                    @debit_dim, @credit_dim,
                    1, false
                );
                """,
                new
                {
                    doc = GuidUtility.DeterministicGuid("glagg-credit-doc", i.ToString()),
                    period = periodUtc,
                    debit = debitCounterparty,
                    credit = creditTargetId,
                    debit_dim = Guid.Empty,
                    credit_dim = Guid.Empty
                });
        }

        await conn.ExecuteAsync("ANALYZE accounting_register_main;");

        return (debitTargetId, creditTargetId);
    }

    private static async Task<JsonElement> ExplainJsonAsync(
        string cs,
        string sql,
        object? args,
        bool disableSeqScan)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        if (disableSeqScan)
            await conn.ExecuteAsync("SET enable_seqscan TO off;");

        // EXPLAIN (FORMAT JSON) returns a single JSON value in the first column.
        // We read it via ADO to avoid any DateOnly/JSON type handler quirks.
        await using var cmd = new NpgsqlCommand("EXPLAIN (FORMAT JSON) " + sql, conn);
        if (args is not null)
        {
            foreach (var p in args.GetType().GetProperties())
            {
                var value = p.GetValue(args);
                cmd.Parameters.AddWithValue(p.Name, value ?? DBNull.Value);
            }
        }

        await using var reader = await cmd.ExecuteReaderAsync(CancellationToken.None);
        await reader.ReadAsync(CancellationToken.None);
        var v = reader.GetValue(0);
        var raw = v switch
        {
            string s => s,
            JsonDocument jd => jd.RootElement.GetRawText(),
            JsonElement je => je.GetRawText(),
            _ => v?.ToString() ?? string.Empty
        };

        using var doc = JsonDocument.Parse(raw);
        // root: [ { "Plan": { ... } } ]
        return doc.RootElement[0].GetProperty("Plan").Clone();
    }

    private static bool PlanContainsIndex(JsonElement plan, string indexName)
    {
        if (plan.ValueKind != JsonValueKind.Object)
            return false;

        if (plan.TryGetProperty("Index Name", out var idx) && idx.ValueKind == JsonValueKind.String)
        {
            if (string.Equals(idx.GetString(), indexName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (plan.TryGetProperty("Plans", out var sub) && sub.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in sub.EnumerateArray())
                if (PlanContainsIndex(child, indexName))
                    return true;
        }

        return false;
    }

    private static bool PlanContainsNodeTypeOnRelation(JsonElement plan, string nodeType, string relationName)
    {
        if (plan.ValueKind != JsonValueKind.Object)
            return false;

        if (plan.TryGetProperty("Node Type", out var nt)
            && nt.ValueKind == JsonValueKind.String
            && string.Equals(nt.GetString(), nodeType, StringComparison.OrdinalIgnoreCase))
        {
            if (plan.TryGetProperty("Relation Name", out var rn)
                && rn.ValueKind == JsonValueKind.String
                && string.Equals(rn.GetString(), relationName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (plan.TryGetProperty("Plans", out var sub) && sub.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in sub.EnumerateArray())
                if (PlanContainsNodeTypeOnRelation(child, nodeType, relationName))
                    return true;
        }

        return false;
    }

    private static class GuidUtility
    {
        public static Guid DeterministicGuid(string scope, string key)
        {
            // Simple deterministic GUID for test data uniqueness.
            // We avoid depending on NGB.Core DeterministicGuid helper to keep tests self-contained.
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(scope + ":" + key);
            var hash = sha.ComputeHash(bytes);
            Span<byte> g = stackalloc byte[16];
            hash.AsSpan(0, 16).CopyTo(g);

            // RFC 4122 version 4 style bits, but deterministic.
            g[6] = (byte)((g[6] & 0x0F) | 0x40);
            g[8] = (byte)((g[8] & 0x3F) | 0x80);
            return new Guid(g);
        }
    }
}
