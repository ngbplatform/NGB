using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: Ordinals define ordering of resources and dimension rules.
/// They must be unique within a register to avoid unstable UX/physical ordering.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterOrdinals_Unique_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ResourceRepository_Replace_WhenOrdinalDuplicates_ThrowsValidationException()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var resRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();

        var regId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 2, 13, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);
        try
        {
            await regRepo.UpsertAsync(new OperationalRegisterUpsert(regId, "RR", "Rent Roll"), nowUtc, CancellationToken.None);

            var act = () => resRepo.ReplaceAsync(
                regId,
                [
                    new OperationalRegisterResourceDefinition("amount", "Amount", 10),
                    new OperationalRegisterResourceDefinition("qty", "Quantity", 10)
                ],
                nowUtc,
                CancellationToken.None);

            var ex = await act.Should().ThrowAsync<OperationalRegisterResourcesValidationException>();
            ex.Which.ErrorCode.Should().Be(OperationalRegisterResourcesValidationException.Code);
            ex.Which.Reason.Should().Be("duplicate_ordinal");
            ex.Which.Context["registerId"].Should().Be(regId);
            ex.Which.Context.Should().ContainKey("collisions");
            ((string[])ex.Which.Context["collisions"]!).Single().Should().StartWith("10:");
        }
        finally
        {
            if (uow.HasActiveTransaction)
                await uow.RollbackAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DbConstraints_PreventDuplicateOrdinalWithinRegister_ForResources()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var regId = Guid.CreateVersion7();
        await conn.ExecuteAsync(
            "INSERT INTO operational_registers(register_id, code, name) VALUES (@Id, 'RR', 'Rent Roll');",
            new { Id = regId });

        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO operational_register_resources(register_id, code, code_norm, column_code, name, ordinal)
                VALUES (@RegId, 'amount', 'amount', 'amount', 'Amount', 10);

                INSERT INTO operational_register_resources(register_id, code, code_norm, column_code, name, ordinal)
                VALUES (@RegId, 'qty', 'qty', 'qty', 'Quantity', 10);
                """,
                new { RegId = regId });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23505");
        ex.Which.ConstraintName.Should().Be("ux_operational_register_resources__register_ordinal");
    }

    [Fact]
    public async Task DimensionRulesRepository_Replace_WhenOrdinalDuplicates_ThrowsValidationException()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var rulesRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterDimensionRuleRepository>();

        var regId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 2, 13, 12, 0, 0, DateTimeKind.Utc);

        var dimId1 = Guid.CreateVersion7();
        var dimId2 = Guid.CreateVersion7();

        await uow.BeginTransactionAsync(CancellationToken.None);
        try
        {
            await regRepo.UpsertAsync(new OperationalRegisterUpsert(regId, "RR", "Rent Roll"), nowUtc, CancellationToken.None);

            var act = () => rulesRepo.ReplaceAsync(
                regId,
                [
                    new OperationalRegisterDimensionRule(dimId1, "Buildings", Ordinal: 10, IsRequired: true),
                    new OperationalRegisterDimensionRule(dimId2, "Units", Ordinal: 10, IsRequired: false)
                ],
                nowUtc,
                CancellationToken.None);

            var ex = await act.Should().ThrowAsync<OperationalRegisterDimensionRulesValidationException>();
            ex.Which.ErrorCode.Should().Be(OperationalRegisterDimensionRulesValidationException.Code);
            ex.Which.Reason.Should().Be("duplicate_ordinal");
            ex.Which.Context["registerId"].Should().Be(regId);
            ex.Which.Context.Should().ContainKey("collisions");
            ((string[])ex.Which.Context["collisions"]!).Single().Should().StartWith("10:");
        }
        finally
        {
            if (uow.HasActiveTransaction)
                await uow.RollbackAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DbConstraints_PreventDuplicateOrdinalWithinRegister_ForDimensionRules()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var regId = Guid.CreateVersion7();
        await conn.ExecuteAsync(
            "INSERT INTO operational_registers(register_id, code, name) VALUES (@Id, 'RR', 'Rent Roll');",
            new { Id = regId });

        var dimId1 = Guid.CreateVersion7();
        var dimId2 = Guid.CreateVersion7();

        await conn.ExecuteAsync(
            """
            INSERT INTO platform_dimensions(dimension_id, code, name) VALUES (@Id1, 'Buildings', 'Buildings');
            INSERT INTO platform_dimensions(dimension_id, code, name) VALUES (@Id2, 'Units', 'Units');
            """,
            new { Id1 = dimId1, Id2 = dimId2 });

        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO operational_register_dimension_rules(register_id, dimension_id, ordinal, is_required)
                VALUES (@RegId, @DimId1, 10, FALSE);

                INSERT INTO operational_register_dimension_rules(register_id, dimension_id, ordinal, is_required)
                VALUES (@RegId, @DimId2, 10, FALSE);
                """,
                new { RegId = regId, DimId1 = dimId1, DimId2 = dimId2 });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23505");
        ex.Which.ConstraintName.Should().Be("ux_opreg_dim_rules__register_ordinal");
    }
}
