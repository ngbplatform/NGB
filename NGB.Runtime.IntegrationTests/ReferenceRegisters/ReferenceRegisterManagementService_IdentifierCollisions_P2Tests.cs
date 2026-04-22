using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Metadata.Base;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterManagementService_IdentifierCollisions_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Upsert_WhenTableCodeCollides_WithAnotherRegister_ThrowsHelpfulError()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

        // These two distinct codes normalize to the same physical table_code in strict SQL identifier normalization:
        //   "A-B" => a_b
        //   "A B" => a_b
        await svc.UpsertAsync(
            code: "A-B",
            name: "Reg 1",
            periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
            recordMode: ReferenceRegisterRecordMode.Independent,
            ct: CancellationToken.None);

        var act = async () =>
        {
            await svc.UpsertAsync(
                code: "A B",
                name: "Reg 2",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);
        };

        var ex = await act.Should().ThrowAsync<ReferenceRegisterTableCodeCollisionException>();
        ex.Which.AssertNgbError(ReferenceRegisterTableCodeCollisionException.Code, "code", "codeNorm", "tableCode", "collidesWithRegisterId");
    }

    [Fact]
    public async Task ReplaceFields_WhenTwoCodesNormalizeToSameColumnCode_ThrowsUniqueViolation()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

        var registerId = await svc.UpsertAsync(
            code: "RR_FIELDS_COLLIDE",
            name: "RR Fields Collide",
            periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
            recordMode: ReferenceRegisterRecordMode.Independent,
            ct: CancellationToken.None);

        // Both codes normalize to the same physical column name (column_code):
        //   "A-B" => a_b
        //   "A B" => a_b
        // Management service validates uniqueness by code_norm and ordinal,
        // but not by normalized column_code (DB PK is (register_id, column_code)).
        var act = async () =>
        {
            await svc.ReplaceFieldsAsync(
                registerId,
                fields:
                [
                    new ReferenceRegisterFieldDefinition(
                        Code: "A-B",
                        Name: "Field 1",
                        Ordinal: 1,
                        ColumnType: ColumnType.String,
                        IsNullable: true),
                    new ReferenceRegisterFieldDefinition(
                        Code: "A B",
                        Name: "Field 2",
                        Ordinal: 2,
                        ColumnType: ColumnType.String,
                        IsNullable: true)
                ],
                ct: CancellationToken.None);
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        ex.Which.ConstraintName.Should().Be("pk_reference_register_fields");
    }
}
