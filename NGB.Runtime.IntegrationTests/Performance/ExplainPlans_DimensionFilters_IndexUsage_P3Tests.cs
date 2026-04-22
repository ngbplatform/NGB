using System.Text.Json;
using Dapper;
using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Performance;

[Collection(PostgresCollection.Name)]
public sealed class ExplainPlans_DimensionFilters_IndexUsage_P3Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task RegisterMonthSlice_WithDimensionFilter_UsesDimensionValueSetIndex_WhenSeqScanDisabled()
    {
        // Ensure schema is bootstrapped.
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var dimId = DeterministicGuid.Create("dim|explain|department");
        var targetValueId = DeterministicGuid.Create("dimval|explain|department|A");

        await EnsureDimensionAsync(Fixture.ConnectionString, dimId, code: "department", name: "Department");
        var seed = await SeedDimensionSetsAsync(Fixture.ConnectionString, dimId, targetValueId, totalSets: 4000, targetSets: 25);

        var cashId = await EnsureAccountAsync(
            Fixture.ConnectionString,
            code: "it_cash_dim",
            name: "IT Cash (Dim)",
            type: AccountType.Asset,
            section: StatementSection.Assets);

        var revenueId = await EnsureAccountAsync(
            Fixture.ConnectionString,
            code: "it_rev_dim",
            name: "IT Revenue (Dim)",
            type: AccountType.Income,
            section: StatementSection.Income);

        await SeedRegisterRowsAsync(
            Fixture.ConnectionString,
            debitAccountId: cashId,
            creditAccountId: revenueId,
            allDebitSets: seed.AllSetIds,
            preferredDebitSetsForTargetMonth: seed.TargetSetIds,
            rows: 9000);

        var plan = await ExplainJsonAsync(
            Fixture.ConnectionString,
            """
            SELECT r.entry_id
            FROM accounting_register_main r
            WHERE r.period_month = @month
              AND r.debit_dimension_set_id IN (
                  SELECT i.dimension_set_id
                  FROM platform_dimension_set_items i
                  WHERE i.dimension_id = @dim_id AND i.value_id = @value_id
              )
            ORDER BY r.period, r.entry_id
            LIMIT 51
            """,
            new
            {
                month = new DateOnly(2025, 6, 1),
                dim_id = dimId,
                value_id = targetValueId
            },
            disableSeqScan: true);

        PlanContainsNodeTypeOnRelation(plan, "Seq Scan", "platform_dimension_set_items").Should().BeFalse();

        PlanContainsIndex(plan, "ix_platform_dimset_items_dimension_value_set").Should().BeTrue(
            "dimension filtering must be index-backed via (dimension_id, value_id, dimension_set_id)");
    }

    [Fact]
    public async Task UnionAllAggregated_WithDimensionFilter_UsesDimensionValueSetIndex_WhenSeqScanDisabled()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var dimId = DeterministicGuid.Create("dim|explain|project");
        var targetValueId = DeterministicGuid.Create("dimval|explain|project|X");

        await EnsureDimensionAsync(Fixture.ConnectionString, dimId, code: "project", name: "Project");
        var seed = await SeedDimensionSetsAsync(Fixture.ConnectionString, dimId, targetValueId, totalSets: 3500, targetSets: 30);

        var cashId = await EnsureAccountAsync(
            Fixture.ConnectionString,
            code: "it_cash_gl",
            name: "IT Cash (GL Dim)",
            type: AccountType.Asset,
            section: StatementSection.Assets);

        var revenueId = await EnsureAccountAsync(
            Fixture.ConnectionString,
            code: "it_rev_gl",
            name: "IT Revenue (GL Dim)",
            type: AccountType.Income,
            section: StatementSection.Income);

        await SeedRegisterRowsAsync(
            Fixture.ConnectionString,
            debitAccountId: cashId,
            creditAccountId: revenueId,
            allDebitSets: seed.AllSetIds,
            preferredDebitSetsForTargetMonth: seed.TargetSetIds,
            rows: 9000);

        var plan = await ExplainJsonAsync(
            Fixture.ConnectionString,
            """
            SELECT x.period_month, x.account_id, x.dimension_set_id,
                   SUM(x.debit_amount) AS debit_amount,
                   SUM(x.credit_amount) AS credit_amount
            FROM (
                SELECT r.period_month,
                       r.debit_account_id AS account_id,
                       r.debit_dimension_set_id AS dimension_set_id,
                       r.amount AS debit_amount,
                       0::numeric AS credit_amount
                FROM accounting_register_main r
                JOIN platform_dimension_set_items i
                  ON i.dimension_set_id = r.debit_dimension_set_id
                 AND i.dimension_id = @dim_id
                 AND i.value_id = @value_id
                WHERE r.period_month = @month

                UNION ALL

                SELECT r.period_month,
                       r.credit_account_id AS account_id,
                       r.credit_dimension_set_id AS dimension_set_id,
                       0::numeric AS debit_amount,
                       r.amount AS credit_amount
                FROM accounting_register_main r
                JOIN platform_dimension_set_items i
                  ON i.dimension_set_id = r.credit_dimension_set_id
                 AND i.dimension_id = @dim_id
                 AND i.value_id = @value_id
                WHERE r.period_month = @month
            ) x
            GROUP BY x.period_month, x.account_id, x.dimension_set_id
            ORDER BY x.period_month, x.account_id, x.dimension_set_id
            LIMIT 51
            """,
            new
            {
                month = new DateOnly(2025, 6, 1),
                dim_id = dimId,
                value_id = targetValueId
            },
            disableSeqScan: true);

        PlanContainsNodeTypeOnRelation(plan, "Seq Scan", "platform_dimension_set_items").Should().BeFalse();
        PlanContainsIndex(plan, "ix_platform_dimset_items_dimension_value_set").Should().BeTrue();
    }

    [Fact]
    public async Task GeneralJournalPage_WithDimensionFilter_UsesMatchingDimensionSetIndex_AndPageOrderIndex_WhenSeqScanDisabled()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var dimId = DeterministicGuid.Create("dim|explain|gj|property");
        var targetValueId = DeterministicGuid.Create("dimval|explain|gj|property|A");

        await EnsureDimensionAsync(Fixture.ConnectionString, dimId, code: "property", name: "Property");
        var seed = await SeedDimensionSetsAsync(Fixture.ConnectionString, dimId, targetValueId, totalSets: 3500, targetSets: 30);

        var cashId = await EnsureAccountAsync(
            Fixture.ConnectionString,
            code: "it_cash_gj",
            name: "IT Cash (GJ Dim)",
            type: AccountType.Asset,
            section: StatementSection.Assets);

        var revenueId = await EnsureAccountAsync(
            Fixture.ConnectionString,
            code: "it_rev_gj",
            name: "IT Revenue (GJ Dim)",
            type: AccountType.Income,
            section: StatementSection.Income);

        await SeedRegisterRowsAsync(
            Fixture.ConnectionString,
            debitAccountId: cashId,
            creditAccountId: revenueId,
            allDebitSets: seed.AllSetIds,
            preferredDebitSetsForTargetMonth: seed.TargetSetIds,
            rows: 9000);

        var plan = await ExplainJsonAsync(
            Fixture.ConnectionString,
            """
            WITH requested_scope_pairs AS (
                SELECT *
                FROM unnest(@dim_ids::uuid[], @value_ids::uuid[]) AS sp(dimension_id, value_id)
            ),
            matching_dimension_sets AS (
                SELECT di.dimension_set_id
                FROM platform_dimension_set_items di
                JOIN requested_scope_pairs sp
                  ON sp.dimension_id = di.dimension_id
                 AND sp.value_id = di.value_id
                GROUP BY di.dimension_set_id
                HAVING COUNT(DISTINCT di.dimension_id) = 1
            )
            SELECT r.entry_id
            FROM accounting_register_main r
            LEFT JOIN matching_dimension_sets debit_scope
              ON debit_scope.dimension_set_id = r.debit_dimension_set_id
            LEFT JOIN matching_dimension_sets credit_scope
              ON credit_scope.dimension_set_id = r.credit_dimension_set_id
            WHERE r.period >= @from_utc
              AND r.period < @to_exclusive_utc
              AND (debit_scope.dimension_set_id IS NOT NULL OR credit_scope.dimension_set_id IS NOT NULL)
            ORDER BY r.period, r.entry_id
            LIMIT 51
            """,
            new
            {
                dim_ids = new[] { dimId },
                value_ids = new[] { targetValueId },
                from_utc = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                to_exclusive_utc = new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            disableSeqScan: true);

        PlanContainsNodeTypeOnRelation(plan, "Seq Scan", "platform_dimension_set_items").Should().BeFalse();
        PlanContainsIndex(plan, "ix_platform_dimset_items_dimension_value_set").Should().BeTrue();
        PlanContainsNodeTypeOnRelation(plan, "Seq Scan", "accounting_register_main").Should().BeFalse();
        PlanContainsIndex(plan, "ix_acc_reg_general_journal_page_order").Should().BeTrue();
    }

    [Fact]
    public async Task AccountCardEffectivePage_WithDimensionFilter_UsesMatchingDimensionSetIndex_And_AccountDimPageOrderIndex_WhenSeqScanDisabled()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var dimId = DeterministicGuid.Create("dim|explain|ac|property");
        var targetValueId = DeterministicGuid.Create("dimval|explain|ac|property|A");

        await EnsureDimensionAsync(Fixture.ConnectionString, dimId, code: "property", name: "Property");
        var seed = await SeedDimensionSetsAsync(Fixture.ConnectionString, dimId, targetValueId, totalSets: 3500, targetSets: 30);

        var cashId = await EnsureAccountAsync(
            Fixture.ConnectionString,
            code: "it_cash_ac",
            name: "IT Cash (AC Dim)",
            type: AccountType.Asset,
            section: StatementSection.Assets);

        var revenueId = await EnsureAccountAsync(
            Fixture.ConnectionString,
            code: "it_rev_ac",
            name: "IT Revenue (AC Dim)",
            type: AccountType.Income,
            section: StatementSection.Income);

        await SeedRegisterRowsAsync(
            Fixture.ConnectionString,
            debitAccountId: cashId,
            creditAccountId: revenueId,
            allDebitSets: seed.AllSetIds,
            preferredDebitSetsForTargetMonth: seed.TargetSetIds,
            rows: 9000);

        var plan = await ExplainJsonAsync(
            Fixture.ConnectionString,
            """
            WITH requested_scope_pairs AS (
                SELECT *
                FROM unnest(@dim_ids::uuid[], @value_ids::uuid[]) AS sp(dimension_id, value_id)
            ),
            matching_dimension_sets AS (
                SELECT di.dimension_set_id
                FROM platform_dimension_set_items di
                JOIN requested_scope_pairs sp
                  ON sp.dimension_id = di.dimension_id
                 AND sp.value_id = di.value_id
                GROUP BY di.dimension_set_id
                HAVING COUNT(DISTINCT di.dimension_id) = 1
            )
            SELECT r.entry_id
            FROM accounting_register_main r
            JOIN matching_dimension_sets debit_scope
              ON debit_scope.dimension_set_id = r.debit_dimension_set_id
            WHERE r.debit_account_id = @acc
              AND r.period >= @from_utc
              AND r.period < @to_exclusive_utc
            ORDER BY r.period, r.entry_id
            LIMIT 51
            """,
            new
            {
                dim_ids = new[] { dimId },
                value_ids = new[] { targetValueId },
                acc = cashId,
                from_utc = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                to_exclusive_utc = new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            disableSeqScan: true);

        PlanContainsNodeTypeOnRelation(plan, "Seq Scan", "platform_dimension_set_items").Should().BeFalse();
        PlanContainsIndex(plan, "ix_platform_dimset_items_dimension_value_set").Should().BeTrue();
        PlanContainsNodeTypeOnRelation(plan, "Seq Scan", "accounting_register_main").Should().BeFalse();
        PlanContainsIndex(plan, "ix_acc_reg_account_card_debit_dim_page_order").Should().BeTrue();
    }

    private sealed record DimensionSeed(Guid[] AllSetIds, Guid[] TargetSetIds);

    private static async Task EnsureDimensionAsync(string cs, Guid dimensionId, string code, string name)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await conn.ExecuteAsync(
            """
            INSERT INTO platform_dimensions(dimension_id, code, name, is_active, is_deleted)
            VALUES(@id, @code, @name, TRUE, FALSE)
            ON CONFLICT (dimension_id) DO NOTHING;
            """,
            new { id = dimensionId, code, name });

        await conn.ExecuteAsync("ANALYZE platform_dimensions;");
    }

    private static async Task<DimensionSeed> SeedDimensionSetsAsync(
        string cs,
        Guid dimensionId,
        Guid targetValueId,
        int totalSets,
        int targetSets)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        // Build a pool of other values so the planner sees selectivity.
        var otherValues = new Guid[60];
        for (var i = 0; i < otherValues.Length; i++)
            otherValues[i] = DeterministicGuid.Create($"dimval|other|{dimensionId:N}|{i}");

        var allSetIds = new Guid[totalSets];
        var dimIds = new Guid[totalSets];
        var valueIds = new Guid[totalSets];

        var targetSetIds = new Guid[Math.Min(targetSets, totalSets)];
        for (var i = 0; i < totalSets; i++)
        {
            var setId = DeterministicGuid.Create($"dimset|explain|{dimensionId:N}|{i}");
            allSetIds[i] = setId;
            dimIds[i] = dimensionId;

            var valueId = i < targetSetIds.Length
                ? targetValueId
                : otherValues[i % otherValues.Length];

            valueIds[i] = valueId;

            if (i < targetSetIds.Length)
                targetSetIds[i] = setId;
        }

        await conn.ExecuteAsync(
            """
            INSERT INTO platform_dimension_sets(dimension_set_id)
            SELECT UNNEST(@SetIds::uuid[])
            ON CONFLICT (dimension_set_id) DO NOTHING;
            """,
            new { SetIds = allSetIds });

        await conn.ExecuteAsync(
            """
            INSERT INTO platform_dimension_set_items(dimension_set_id, dimension_id, value_id)
            SELECT u.dimension_set_id, u.dimension_id, u.value_id
            FROM UNNEST(@SetIds::uuid[], @DimIds::uuid[], @ValueIds::uuid[])
                 AS u(dimension_set_id, dimension_id, value_id)
            ON CONFLICT DO NOTHING;
            """,
            new
            {
                SetIds = allSetIds,
                DimIds = dimIds,
                ValueIds = valueIds
            });

        await conn.ExecuteAsync("ANALYZE platform_dimension_sets;");
        await conn.ExecuteAsync("ANALYZE platform_dimension_set_items;");

        return new DimensionSeed(allSetIds, targetSetIds);
    }

    private static async Task SeedRegisterRowsAsync(
        string cs,
        Guid debitAccountId,
        Guid creditAccountId,
        Guid[] allDebitSets,
        Guid[] preferredDebitSetsForTargetMonth,
        int rows)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        // Spread across 24 months (2024-01..2025-12) so month slicing is selective.
        var docs = new Guid[rows];
        var periods = new DateTime[rows];
        var debitSets = new Guid[rows];
        var creditSets = new Guid[rows];

        for (var i = 0; i < rows; i++)
        {
            docs[i] = DeterministicGuid.Create($"doc|explain|{i}");
            var m = i % 24;
            var year = 2024 + (m / 12);
            var month = (m % 12) + 1;
            var day = (i % 28) + 1;
            periods[i] = new DateTime(year, month, day, 12, 0, 0, DateTimeKind.Utc);

            // Default: any set.
            var set = allDebitSets[i % allDebitSets.Length];

            // For the target month (2025-06), ensure some rows use matching sets to keep join estimates realistic.
            if (year == 2025 && month == 6 && preferredDebitSetsForTargetMonth.Length > 0 && (i % 10 == 0))
                set = preferredDebitSetsForTargetMonth[i % preferredDebitSetsForTargetMonth.Length];

            debitSets[i] = set;
            creditSets[i] = Guid.Empty; // empty set row exists in platform_dimension_sets
        }

        await conn.ExecuteAsync(
            """
            INSERT INTO accounting_register_main(
                document_id, period,
                debit_account_id, credit_account_id,
                debit_dimension_set_id, credit_dimension_set_id,
                amount, is_storno)
            SELECT u.document_id, u.period,
                   @debit, @credit,
                   u.debit_set, u.credit_set,
                   1, FALSE
            FROM UNNEST(
                @Docs::uuid[],
                @Periods::timestamptz[],
                @DebitSets::uuid[],
                @CreditSets::uuid[]
            ) AS u(document_id, period, debit_set, credit_set);
            """,
            new
            {
                Docs = docs,
                Periods = periods,
                DebitSets = debitSets,
                CreditSets = creditSets,
                debit = debitAccountId,
                credit = creditAccountId
            });

        await conn.ExecuteAsync("ANALYZE accounting_register_main;");
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
                FALSE,
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
}
