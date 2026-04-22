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
/// P0: Resources are used to create physical columns in per-register tables,
/// so we must defend against collisions and reserved names.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterResources_Constraints_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ResourceRepository_Replace_WhenCodeNormCollides_ThrowsValidationException()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var resRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();

        var regId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 1, 30, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);
        try
        {
            await regRepo.UpsertAsync(new OperationalRegisterUpsert(regId, "RR", "Rent Roll"), nowUtc, CancellationToken.None);

            var act = () => resRepo.ReplaceAsync(
                regId,
                [
                    new OperationalRegisterResourceDefinition("PRICE", "Price", 10),
                    new OperationalRegisterResourceDefinition("  price  ", "Price (duplicate)", 20)
                ],
                nowUtc,
                CancellationToken.None);

            var ex = await act.Should().ThrowAsync<OperationalRegisterResourcesValidationException>();
            ex.Which.ErrorCode.Should().Be(OperationalRegisterResourcesValidationException.Code);
            ex.Which.Reason.Should().Be("code_norm_collisions");
            ((string[])ex.Which.Context["collisions"]!).Single().Should().Contain("price");
        }
        finally
        {
            if (uow.HasActiveTransaction)
                await uow.RollbackAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ResourceRepository_Replace_WhenColumnCodeCollides_ThrowsValidationException()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var resRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();

        var regId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 1, 30, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);
        try
        {
            await regRepo.UpsertAsync(new OperationalRegisterUpsert(regId, "RR", "Rent Roll"), nowUtc, CancellationToken.None);

            // code_norm differs ("a-b" vs "a_b"), but NormalizeColumnCode produces the same physical column: "a_b".
            var act = () => resRepo.ReplaceAsync(
                regId,
                [
                    new OperationalRegisterResourceDefinition("A-B", "A-B", 10),
                    new OperationalRegisterResourceDefinition("A_B", "A_B", 20)
                ],
                nowUtc,
                CancellationToken.None);

            var ex = await act.Should().ThrowAsync<OperationalRegisterResourcesValidationException>();
            ex.Which.ErrorCode.Should().Be(OperationalRegisterResourcesValidationException.Code);
            ex.Which.Reason.Should().Be("column_code_collisions");
            ((string[])ex.Which.Context["collisions"]!).Single().Should().Contain("a_b");
        }
        finally
        {
            if (uow.HasActiveTransaction)
                await uow.RollbackAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ResourceRepository_Replace_WhenReservedColumnNameIsUsed_ThrowsValidationException()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var resRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();

        var regId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 1, 30, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);
        try
        {
            await regRepo.UpsertAsync(new OperationalRegisterUpsert(regId, "RR", "Rent Roll"), nowUtc, CancellationToken.None);

            var act = () => resRepo.ReplaceAsync(
                regId,
                [
                    new OperationalRegisterResourceDefinition("document_id", "Bad", 10),
                    new OperationalRegisterResourceDefinition("amount", "Ok", 20)
                ],
                nowUtc,
                CancellationToken.None);

            var ex = await act.Should().ThrowAsync<OperationalRegisterResourcesValidationException>();
            ex.Which.ErrorCode.Should().Be(OperationalRegisterResourcesValidationException.Code);
            ex.Which.Reason.Should().Be("reserved_column_code");
            ((string[])ex.Which.Context["columnCodes"]!).Should().Contain("document_id");
        }
        finally
        {
            if (uow.HasActiveTransaction)
                await uow.RollbackAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DbConstraints_PreventDuplicateColumnCodeWithinRegister()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var regId = Guid.CreateVersion7();
        await conn.ExecuteAsync(
            "INSERT INTO operational_registers(register_id, code, name) VALUES (@Id, 'RR', 'Rent Roll');",
            new { Id = regId });

        // Two different resource codes can normalize to the same physical column_code.
        // The DB must still protect us (pk_operational_register_resources).
        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO operational_register_resources(register_id, code, code_norm, column_code, name, ordinal)
                VALUES (@RegId, 'A-B', 'a-b', 'a_b', 'First', 10);

                INSERT INTO operational_register_resources(register_id, code, code_norm, column_code, name, ordinal)
                VALUES (@RegId, 'A_B', 'a_b', 'a_b', 'Second', 20);
                """,
                new { RegId = regId });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23505");
        ex.Which.ConstraintName.Should().Be("pk_operational_register_resources");
    }

    [Fact]
    public async Task DbConstraints_PreventDuplicateCodeNormWithinRegister()
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
                VALUES (@RegId, 'PRICE', 'price', 'price', 'First', 10);

                INSERT INTO operational_register_resources(register_id, code, code_norm, column_code, name, ordinal)
                VALUES (@RegId, '  price  ', 'price', 'price_2', 'Second', 20);
                """,
                new { RegId = regId });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23505");
        ex.Which.ConstraintName.Should().Be("ux_operational_register_resources__register_code_norm");
    }

    [Fact]
    public async Task DbConstraints_PreventReservedColumnCodeWithinRegister()
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
                VALUES (@RegId, 'document_id', 'document_id', 'document_id', 'Bad', 10);
                """,
                new { RegId = regId });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be("ck_operational_register_resources__column_code_not_reserved");
    }


    [Fact]
    public async Task DbConstraints_PreventColumnCodeStartingWithDigit()
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
                VALUES (@RegId, '1ST', '1st', '1st', 'Bad', 10);
                """,
                new { RegId = regId });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be("ck_operational_register_resources__column_code_safe");
    }
}
