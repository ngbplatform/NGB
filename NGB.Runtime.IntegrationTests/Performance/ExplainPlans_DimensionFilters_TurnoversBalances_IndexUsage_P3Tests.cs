using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Performance;

[Collection(PostgresCollection.Name)]
public sealed class ExplainPlans_DimensionFilters_TurnoversBalances_IndexUsage_P3Tests : IntegrationTestBase
{
    public ExplainPlans_DimensionFilters_TurnoversBalances_IndexUsage_P3Tests(PostgresTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task TurnoversMonthQuery_WithDimensionValueFilter_IsIndexBacked_WhenSeqScanDisabled()
    {
        var (accountId, dimensionId, hitValueId) = await SeedAsync(Fixture.ConnectionString);

        var plan = await ExplainJsonAsync(
            Fixture.ConnectionString,
            """
            WITH sets AS (
                SELECT dimension_set_id
                FROM platform_dimension_set_items
                WHERE dimension_id = @dim::uuid AND value_id = @val::uuid
            )
            SELECT t.period, t.account_id, t.dimension_set_id, t.debit_amount, t.credit_amount
            FROM accounting_turnovers t
            JOIN sets s ON s.dimension_set_id = t.dimension_set_id
            WHERE t.period = @period::date
              AND t.account_id = @acc::uuid
            ORDER BY t.dimension_set_id
            LIMIT 51;
            """,
            new
            {
                dim = dimensionId,
                val = hitValueId,
                period = new DateTime(2025, 6, 1),
                acc = accountId
            },
            disableSeqScan: true);

        PlanContainsNodeTypeOnRelation(plan, "Seq Scan", "platform_dimension_set_items").Should().BeFalse();
        PlanContainsNodeTypeOnRelation(plan, "Seq Scan", "accounting_turnovers").Should().BeFalse();

        PlanContainsIndex(plan, "ix_platform_dimset_items_dimension_value_set")
            .Should().BeTrue("dimension value filtering must be index-backed");

        (PlanContainsIndex(plan, "accounting_turnovers_pkey")
         || PlanContainsIndex(plan, "ix_acc_turnovers_period_account")
         || PlanContainsIndex(plan, "ix_acc_turnovers_account_period"))
            .Should().BeTrue("turnovers lookup for a single month must be index-backed");
    }

    [Fact]
    public async Task BalancesMonthQuery_WithDimensionValueFilter_IsIndexBacked_WhenSeqScanDisabled()
    {
        var (accountId, dimensionId, hitValueId) = await SeedAsync(Fixture.ConnectionString);

        var plan = await ExplainJsonAsync(
            Fixture.ConnectionString,
            """
            WITH sets AS (
                SELECT dimension_set_id
                FROM platform_dimension_set_items
                WHERE dimension_id = @dim::uuid AND value_id = @val::uuid
            )
            SELECT b.period, b.account_id, b.dimension_set_id, b.opening_balance, b.closing_balance
            FROM accounting_balances b
            JOIN sets s ON s.dimension_set_id = b.dimension_set_id
            WHERE b.period = @period::date
              AND b.account_id = @acc::uuid
            ORDER BY b.dimension_set_id
            LIMIT 51;
            """,
            new
            {
                dim = dimensionId,
                val = hitValueId,
                period = new DateTime(2025, 6, 1),
                acc = accountId
            },
            disableSeqScan: true);

        PlanContainsNodeTypeOnRelation(plan, "Seq Scan", "platform_dimension_set_items").Should().BeFalse();
        PlanContainsNodeTypeOnRelation(plan, "Seq Scan", "accounting_balances").Should().BeFalse();

        PlanContainsIndex(plan, "ix_platform_dimset_items_dimension_value_set")
            .Should().BeTrue("dimension value filtering must be index-backed");

        (PlanContainsIndex(plan, "accounting_balances_pkey")
         || PlanContainsIndex(plan, "ix_acc_balances_period_account")
         || PlanContainsIndex(plan, "ix_acc_balances_account_period"))
            .Should().BeTrue("balances lookup for a single month must be index-backed");
    }

    private static async Task<(Guid accountId, Guid dimensionId, Guid hitValueId)> SeedAsync(string cs)
    {
        var accountId = await EnsureAccountAsync(
            cs,
            code: "it_dim_cash",
            name: "IT Cash",
            type: AccountType.Asset,
            section: StatementSection.Assets);

        var dimensionId = await EnsureDimensionAsync(cs, code: "it_dim", name: "IT Dimension");

        var hitValueId = DeterministicGuid("dimval", "hit");
        var missValueId = DeterministicGuid("dimval", "miss");

        const int setCount = 800;
        var setIdsHit = new Guid[setCount / 2];
        var setIdsMiss = new Guid[setCount / 2];

        for (int i = 0; i < setIdsHit.Length; i++)
            setIdsHit[i] = DeterministicGuid("dimset_hit", i.ToString());

        for (int i = 0; i < setIdsMiss.Length; i++)
            setIdsMiss[i] = DeterministicGuid("dimset_miss", i.ToString());

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await SeedDimensionSetsAsync(conn, dimensionId, hitValueId, setIdsHit);
        await SeedDimensionSetsAsync(conn, dimensionId, missValueId, setIdsMiss);

        var from = new DateOnly(2024, 1, 1);
        const int months = 24;

        for (int m = 0; m < months; m++)
        {
            var periodDateTime = from.AddMonths(m).ToDateTime(TimeOnly.MinValue);

            await SeedTurnoversForMonthAsync(conn, periodDateTime, accountId, setIdsHit);
            await SeedTurnoversForMonthAsync(conn, periodDateTime, accountId, setIdsMiss);

            await SeedBalancesForMonthAsync(conn, periodDateTime, accountId, setIdsHit);
            await SeedBalancesForMonthAsync(conn, periodDateTime, accountId, setIdsMiss);
        }

        await conn.ExecuteAsync("ANALYZE platform_dimension_set_items;");
        await conn.ExecuteAsync("ANALYZE accounting_turnovers;");
        await conn.ExecuteAsync("ANALYZE accounting_balances;");

        return (accountId, dimensionId, hitValueId);
    }

    private static async Task<Guid> EnsureDimensionAsync(string cs, string code, string name)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        var existing = await conn.ExecuteScalarAsync<Guid?>(
            """
            SELECT dimension_id
            FROM platform_dimensions
            WHERE code_norm = lower(btrim(@code))
              AND is_deleted = FALSE
            LIMIT 1;
            """,
            new { code });

        if (existing is not null)
            return existing.Value;

        var id = Guid.CreateVersion7();
        await conn.ExecuteAsync(
            """
            INSERT INTO platform_dimensions(
                dimension_id, code, name,
                is_active, is_deleted,
                created_at_utc, updated_at_utc)
            VALUES(
                @id, @code, @name,
                TRUE, FALSE,
                NOW(), NOW()
            );
            """,
            new { id, code, name });

        return id;
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
            WHERE code = @code AND is_deleted = FALSE
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

    private static async Task SeedDimensionSetsAsync(
        NpgsqlConnection conn,
        Guid dimensionId,
        Guid valueId,
        Guid[] setIds)
    {
        await conn.ExecuteAsync(
            """
            INSERT INTO platform_dimension_sets(dimension_set_id)
            SELECT s
            FROM unnest(@ids::uuid[]) s
            ON CONFLICT (dimension_set_id) DO NOTHING;
            """,
            new { ids = setIds });

        await conn.ExecuteAsync(
            """
            INSERT INTO platform_dimension_set_items(dimension_set_id, dimension_id, value_id)
            SELECT s, @dim::uuid, @val::uuid
            FROM unnest(@ids::uuid[]) s
            ON CONFLICT (dimension_set_id, dimension_id) DO NOTHING;
            """,
            new { ids = setIds, dim = dimensionId, val = valueId });
    }

    private static Task SeedTurnoversForMonthAsync(
        NpgsqlConnection conn,
        DateTime period,
        Guid accountId,
        Guid[] setIds)
        => conn.ExecuteAsync(
            """
            INSERT INTO accounting_turnovers(
                period, account_id, dimension_set_id,
                debit_amount, credit_amount)
            SELECT @period::date, @acc::uuid, s,
                   1::numeric, 0::numeric
            FROM unnest(@ids::uuid[]) s
            ON CONFLICT (period, account_id, dimension_set_id) DO NOTHING;
            """,
            new { period, acc = accountId, ids = setIds });

    private static Task SeedBalancesForMonthAsync(
        NpgsqlConnection conn,
        DateTime period,
        Guid accountId,
        Guid[] setIds)
        => conn.ExecuteAsync(
            """
            INSERT INTO accounting_balances(
                period, account_id, dimension_set_id,
                opening_balance, closing_balance)
            SELECT @period::date, @acc::uuid, s,
                   0::numeric, 1::numeric
            FROM unnest(@ids::uuid[]) s
            ON CONFLICT (period, account_id, dimension_set_id) DO NOTHING;
            """,
            new { period, acc = accountId, ids = setIds });

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

    private static Guid DeterministicGuid(string scope, string key)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(scope + ":" + key);
        var hash = sha.ComputeHash(bytes);

        Span<byte> g = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(g);

        g[6] = (byte)((g[6] & 0x0F) | 0x40);
        g[8] = (byte)((g[8] & 0x3F) | 0x80);

        return new Guid(g);
    }
}
