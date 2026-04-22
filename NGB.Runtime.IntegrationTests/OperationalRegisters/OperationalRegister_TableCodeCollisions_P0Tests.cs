using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.OperationalRegisters;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: Fail fast on table name collisions (table_code) for operational registers.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegister_TableCodeCollisions_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Upsert_WhenDifferentCodeNormsMapToSameTableCode_ThrowsFailFast_AndDoesNotInsertSecondRegister()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
        var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();

        // code_norm differs: "a-b" vs "a_b".
        // table_code collides: both normalize to "a_b".
        var id1 = await svc.UpsertAsync("a-b", "Register A", CancellationToken.None);
        var id2 = OperationalRegisterId.FromCode("a_b");

        id1.Should().NotBe(id2, "register id is derived from code_norm, and these code_norm values are different");

        var act = async () => await svc.UpsertAsync("a_b", "Register B", CancellationToken.None);

        var ex = await act.Should().ThrowAsync<OperationalRegisterTableCodeCollisionException>();
        ex.Which.AssertNgbError(OperationalRegisterTableCodeCollisionException.Code, "code", "codeNorm", "tableCode", "collidesWithRegisterId");

        var byCode = await repo.GetByCodeAsync("a_b", CancellationToken.None);
        byCode.Should().BeNull("the second register must not be inserted when table_code collides");
    }
}
