using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Exceptions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: Deterministic IDs must remain meaningful.
/// If an existing register row has a register_id that does not match the stored code/code_norm,
/// we should fail fast with a custom conflict exception.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterManagementService_CodeNormMismatch_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Upsert_WhenExistingRowHasDifferentCodeNormForSameRegisterId_ThrowsConflictException()
    {
        // Arrange
        var registerId = OperationalRegisterId.FromCode("RR");

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            // Insert an inconsistent row: register_id corresponds to code_norm("RR") => "rr",
            // but the stored code is different => code_norm("ZZ") => "zz".
            await conn.ExecuteAsync(
                "INSERT INTO operational_registers(register_id, code, name) VALUES (@Id, @Code, @Name);",
                new { Id = registerId, Code = "ZZ", Name = "Corrupted" });
        }

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();

        // Act
        var act = () => svc.UpsertAsync("RR", "Rent Roll");

        // Assert
        var ex = await act.Should().ThrowAsync<OperationalRegisterCodeNormMismatchException>();
        ex.Which.RegisterId.Should().Be(registerId);
        ex.Which.AssertNgbError(
            OperationalRegisterCodeNormMismatchException.Code,
            "registerId",
            "attemptedCode",
            "attemptedCodeNorm",
            "existingCode",
            "existingCodeNorm");
    }
}
