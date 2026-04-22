using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// DB-level guards for Operational Registers metadata.
///
/// These tests deliberately bypass Runtime services and mutate metadata tables directly,
/// asserting that PostgreSQL triggers enforce production invariants once has_movements=true.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DatabaseGuards_OperationalRegisters_MetadataGuards_WhenHasMovements_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Registers_AreImmutableAfterHasMovements_And_HasMovementsIsMonotonic()
    {
        var registerId = await SeedRegisterWithOneMovementAsync(includeResources: false, includeDimensionRules: false);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        // Name may change (allowed).
        await conn.ExecuteAsync(
            "UPDATE operational_registers SET name = @Name WHERE register_id = @Id;",
            new { Id = registerId, Name = "OpReg Guard Renamed" });

        var name = await conn.ExecuteScalarAsync<string?>(
            "SELECT name FROM operational_registers WHERE register_id = @Id;",
            new { Id = registerId });

        name.Should().Be("OpReg Guard Renamed");

        // Code/table_code/code_norm are immutable once has_movements=true.
        await AssertForbiddenAsync(() => conn.ExecuteAsync(
            "UPDATE operational_registers SET code = @Code WHERE register_id = @Id;",
            new { Id = registerId, Code = "it_opreg_guard_new" }));

        // NOTE: table_code/code_norm are GENERATED ALWAYS columns and cannot be updated directly (planner-level error).
        // We only assert the business invariant on the base column (code) which is protected by DB trigger when has_movements=true.


        // has_movements can never flip back.
        await AssertForbiddenAsync(() => conn.ExecuteAsync(
            "UPDATE operational_registers SET has_movements = FALSE WHERE register_id = @Id;",
            new { Id = registerId }));

        // Delete is forbidden after movements exist.
        await AssertForbiddenAsync(() => conn.ExecuteAsync(
            "DELETE FROM operational_registers WHERE register_id = @Id;",
            new { Id = registerId }));
    }

    [Fact]
    public async Task Resources_AreImmutableAfterHasMovements_But_NameAndOrdinalMayChange()
    {
        var registerId = await SeedRegisterWithOneMovementAsync(includeResources: true, includeDimensionRules: false);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        // Name/ordinal are allowed.
        await conn.ExecuteAsync(
            "UPDATE operational_register_resources SET name = @Name, ordinal = @Ord WHERE register_id = @Id AND column_code = @ColumnCode;",
            new { Id = registerId, ColumnCode = "amount", Name = "Amount Renamed", Ord = 20 });

        var row = await conn.QuerySingleAsync<(string Name, int Ordinal)>(
            "SELECT name AS Name, ordinal AS Ordinal FROM operational_register_resources WHERE register_id = @Id AND column_code = @ColumnCode;",
            new { Id = registerId, ColumnCode = "amount" });

        row.Name.Should().Be("Amount Renamed");
        row.Ordinal.Should().Be(20);

        // Identifiers are immutable after movements exist.
        await AssertForbiddenAsync(() => conn.ExecuteAsync(
            "UPDATE operational_register_resources SET column_code = @C WHERE register_id = @Id AND column_code = @ColumnCode;",
            new { Id = registerId, ColumnCode = "amount", C = "amount2" }));

        // Delete is forbidden.
        await AssertForbiddenAsync(() => conn.ExecuteAsync(
            "DELETE FROM operational_register_resources WHERE register_id = @Id AND column_code = @ColumnCode;",
            new { Id = registerId, ColumnCode = "amount" }));
    }

    [Fact]
    public async Task DimensionRules_AreAppendOnlyAfterHasMovements_But_OptionalMayBeAdded()
    {
        var registerId = await SeedRegisterWithOneMovementAsync(includeResources: true, includeDimensionRules: true);

        var dimA = DeterministicGuid.Create("Dimension|it_opreg_dim_a");
        var dimB = DeterministicGuid.Create("Dimension|it_opreg_dim_b");
        var dimC = DeterministicGuid.Create("Dimension|it_opreg_dim_c");

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        // DELETE / UPDATE are forbidden after has_movements=true.
        await AssertForbiddenAsync(() => conn.ExecuteAsync(
            "DELETE FROM operational_register_dimension_rules WHERE register_id = @R AND dimension_id = @D;",
            new { R = registerId, D = dimA }));

        await AssertForbiddenAsync(() => conn.ExecuteAsync(
            "UPDATE operational_register_dimension_rules SET ordinal = @O WHERE register_id = @R AND dimension_id = @D;",
            new { R = registerId, D = dimA, O = 99 }));

        // Prepare new dimensions (to avoid FK failures and ensure the trigger is the reason for rejection).
        await EnsureDimensionExistsAsync(conn, dimB, "it_opreg_dim_b");
        await EnsureDimensionExistsAsync(conn, dimC, "it_opreg_dim_c");

        // Adding REQUIRED rule after movements exist is forbidden.
        await AssertForbiddenAsync(() => conn.ExecuteAsync(
            "INSERT INTO operational_register_dimension_rules(register_id, dimension_id, ordinal, is_required) VALUES (@R, @D, @O, TRUE);",
            new { R = registerId, D = dimC, O = 30 }));

        // Adding OPTIONAL rule is allowed.
        await conn.ExecuteAsync(
            "INSERT INTO operational_register_dimension_rules(register_id, dimension_id, ordinal, is_required) VALUES (@R, @D, @O, FALSE);",
            new { R = registerId, D = dimB, O = 20 });

        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM operational_register_dimension_rules WHERE register_id = @R;",
            new { R = registerId });

        count.Should().Be(2);
    }

    private async Task<Guid> SeedRegisterWithOneMovementAsync(bool includeResources, bool includeDimensionRules)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var management = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var movementsStore = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>();

        var suffix = Guid.CreateVersion7().ToString("N")[..8];
        var code = $"it_opreg_guard_{suffix}";
        var registerId = await management.UpsertAsync(code, code);

        if (includeResources)
        {
            await management.ReplaceResourcesAsync(
                registerId,
                [
                    new OperationalRegisterResourceDefinition(
                        Code: "amount",
                        Name: "Amount",
                        Ordinal: 10)
                ]);
        }

        if (includeDimensionRules)
        {
            await management.ReplaceDimensionRulesAsync(
                registerId,
                [
                    new OperationalRegisterDimensionRule(
                        DimensionId: DeterministicGuid.Create("Dimension|it_opreg_dim_a"),
                        DimensionCode: "it_opreg_dim_a",
                        Ordinal: 10,
                        IsRequired: true)
                ]);
        }

        var nowUtc = DateTime.UtcNow;

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await movementsStore.EnsureSchemaAsync(registerId, ct);

            var resources = includeResources
                ? new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 10m }
                : new Dictionary<string, decimal>(StringComparer.Ordinal);

            await movementsStore.AppendAsync(
                registerId,
                [
                    new OperationalRegisterMovement(
                        DocumentId: Guid.CreateVersion7(),
                        OccurredAtUtc: nowUtc,
                        DimensionSetId: Guid.Empty,
                        Resources: resources)
                ],
                ct);
        });

        // Sanity check: has_movements must be true after at least one movement is written.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var has = await conn.ExecuteScalarAsync<bool>(
                "SELECT has_movements FROM operational_registers WHERE register_id = @Id;",
                new { Id = registerId });

            has.Should().BeTrue();
        }

        return registerId;
    }

    private static async Task EnsureDimensionExistsAsync(NpgsqlConnection conn, Guid dimensionId, string code)
    {
        var now = DateTime.UtcNow;

        await conn.ExecuteAsync(
            """
            INSERT INTO platform_dimensions(dimension_id, code, name, is_active, is_deleted, created_at_utc, updated_at_utc)
            VALUES (@Id, @Code, @Name, TRUE, FALSE, @Now, @Now)
            ON CONFLICT (dimension_id) DO NOTHING;
            """,
            new { Id = dimensionId, Code = code, Name = code, Now = now });
    }

    private static async Task AssertForbiddenAsync(Func<Task> act)
    {
        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().BeOneOf("55000", "P0001");
    }
}
