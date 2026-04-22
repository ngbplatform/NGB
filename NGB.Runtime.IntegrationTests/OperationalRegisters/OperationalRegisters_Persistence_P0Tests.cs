using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: PostgreSQL roundtrip for operational register persistence contracts.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisters_Persistence_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task OperationalRegister_Upsert_And_Read_Roundtrip()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();

        var regId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);
        await repo.UpsertAsync(new OperationalRegisterUpsert(
            RegisterId: regId,
            Code: "  RENT_ROLL ",
            Name: "Rent Roll"), nowUtc, CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);

        var byCode = await repo.GetByCodeAsync("rent_roll", CancellationToken.None);
        byCode.Should().NotBeNull();
        byCode!.RegisterId.Should().Be(regId);
        byCode.Code.Should().Be("RENT_ROLL", "code is trimmed but case is preserved");
        byCode.CodeNorm.Should().Be("rent_roll");
        byCode.Name.Should().Be("Rent Roll");

        var byId = await repo.GetByIdAsync(regId, CancellationToken.None);
        byId.Should().NotBeNull();
        byId!.CodeNorm.Should().Be("rent_roll");

        var all = await repo.GetAllAsync(CancellationToken.None);
        all.Should().ContainSingle(x => x.RegisterId == regId);
    }

    [Fact]
    public async Task OperationalRegisterDimensionRules_Replace_And_Read_Roundtrip()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var rulesRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterDimensionRuleRepository>();

        var regId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        var dimId1 = Guid.CreateVersion7();
        var dimId2 = Guid.CreateVersion7();

        await uow.BeginTransactionAsync(CancellationToken.None);

        await regRepo.UpsertAsync(new OperationalRegisterUpsert(regId, "RR", "Rent Roll"), nowUtc, CancellationToken.None);

        // Seed dimensions referenced by rules.
        await uow.Connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO platform_dimensions(dimension_id, code, name) VALUES (@Id, @Code, @Name);",
            new[]
            {
                new { Id = dimId1, Code = "Buildings", Name = "Buildings" },
                new { Id = dimId2, Code = "Units", Name = "Units" }
            },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        await rulesRepo.ReplaceAsync(regId,
        [
            new OperationalRegisterDimensionRule(dimId1, "Buildings", Ordinal: 10, IsRequired: true),
            new OperationalRegisterDimensionRule(dimId2, "Units", Ordinal: 20, IsRequired: false)
        ], nowUtc, CancellationToken.None);

        await uow.CommitAsync(CancellationToken.None);

        var rules = await rulesRepo.GetByRegisterIdAsync(regId, CancellationToken.None);
        rules.Should().HaveCount(2);
        rules[0].DimensionId.Should().Be(dimId1);
        rules[0].DimensionCode.Should().Be("Buildings");
        rules[0].Ordinal.Should().Be(10);
        rules[0].IsRequired.Should().BeTrue();

        rules[1].DimensionId.Should().Be(dimId2);
        rules[1].DimensionCode.Should().Be("Units");
        rules[1].Ordinal.Should().Be(20);
        rules[1].IsRequired.Should().BeFalse();
    }

    [Fact]
    public async Task OperationalRegisterFinalization_MarkDirty_Then_Finalized_Roundtrip()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var finRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

        var regId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        var period = new DateOnly(2026, 1, 1);

        await uow.BeginTransactionAsync(CancellationToken.None);
        await regRepo.UpsertAsync(new OperationalRegisterUpsert(regId, "RR", "Rent Roll"), nowUtc, CancellationToken.None);

        var dirtySinceUtc = nowUtc.AddMinutes(-5);
        await finRepo.MarkDirtyAsync(regId, period, dirtySinceUtc, nowUtc, CancellationToken.None);

        await uow.CommitAsync(CancellationToken.None);

        var dirty = await finRepo.GetAsync(regId, period, CancellationToken.None);
        dirty.Should().NotBeNull();
        dirty!.Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty);
        dirty.DirtySinceUtc.Should().NotBeNull();
        dirty.DirtySinceUtc.Should().BeCloseTo(dirtySinceUtc, precision: TimeSpan.FromSeconds(1));
        dirty.FinalizedAtUtc.Should().BeNull();

        // Mark finalized.
        await uow.BeginTransactionAsync(CancellationToken.None);
        var finalizedAtUtc = nowUtc.AddMinutes(1);
        await finRepo.MarkFinalizedAsync(regId, period, finalizedAtUtc, nowUtc.AddMinutes(1), CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);

        var fin = await finRepo.GetAsync(regId, period, CancellationToken.None);
        fin.Should().NotBeNull();
        fin!.Status.Should().Be(OperationalRegisterFinalizationStatus.Finalized);
        fin.FinalizedAtUtc.Should().NotBeNull();
        fin.FinalizedAtUtc.Should().BeCloseTo(finalizedAtUtc, precision: TimeSpan.FromSeconds(1));
        fin.DirtySinceUtc.Should().BeNull();
    }
}
