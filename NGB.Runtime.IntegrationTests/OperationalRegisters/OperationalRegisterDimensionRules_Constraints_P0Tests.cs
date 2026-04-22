using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: Dimension rules define the physical analytics schema... etc.
/// We validate inputs and throw custom validation exceptions with stable reason codes.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterDimensionRulesConstraintsP0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task DimensionRuleRepository_Replace_WhenDimensionCodeEmpty_ThrowsValidationException()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var rulesRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterDimensionRuleRepository>();

        var regId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 2, 13, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);
        try
        {
            await regRepo.UpsertAsync(new OperationalRegisterUpsert(regId, "RR", "Rent Roll"), nowUtc, CancellationToken.None);

            var act = () => rulesRepo.ReplaceAsync(
                regId,
                [
                    new OperationalRegisterDimensionRule(Guid.CreateVersion7(), "   ", 10, IsRequired: false)
                ],
                nowUtc,
                CancellationToken.None);

            var ex = await act.Should().ThrowAsync<OperationalRegisterDimensionRulesValidationException>();
            ex.Which.ErrorCode.Should().Be(OperationalRegisterDimensionRulesValidationException.Code);
            ex.Which.Reason.Should().Be("empty_dimension_code");
            ex.Which.Context["registerId"].Should().Be(regId);
            ex.Which.Context["index"].Should().Be(0);
        }
        finally
        {
            if (uow.HasActiveTransaction)
                await uow.RollbackAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DimensionRuleRepository_Replace_WhenSameDimensionIdHasConflictingDefinitions_ThrowsValidationException()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var rulesRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterDimensionRuleRepository>();

        var regId = Guid.CreateVersion7();
        var dimId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 2, 13, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);
        try
        {
            await regRepo.UpsertAsync(new OperationalRegisterUpsert(regId, "RR", "Rent Roll"), nowUtc, CancellationToken.None);

            var act = () => rulesRepo.ReplaceAsync(
                regId,
                [
                    new OperationalRegisterDimensionRule(dimId, "building", 10, IsRequired: false),
                    new OperationalRegisterDimensionRule(dimId, "building", 20, IsRequired: true)
                ],
                nowUtc,
                CancellationToken.None);

            var ex = await act.Should().ThrowAsync<OperationalRegisterDimensionRulesValidationException>();
            ex.Which.ErrorCode.Should().Be(OperationalRegisterDimensionRulesValidationException.Code);
            ex.Which.Reason.Should().Be("duplicate_dimension_id_conflict");
            ex.Which.Context["registerId"].Should().Be(regId);
            ex.Which.Context["dimensionId"].Should().Be(dimId);
        }
        finally
        {
            if (uow.HasActiveTransaction)
                await uow.RollbackAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DimensionRuleRepository_Replace_WhenOrdinalsCollide_ThrowsValidationException()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var rulesRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterDimensionRuleRepository>();

        var regId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 2, 13, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);
        try
        {
            await regRepo.UpsertAsync(new OperationalRegisterUpsert(regId, "RR", "Rent Roll"), nowUtc, CancellationToken.None);

            var act = () => rulesRepo.ReplaceAsync(
                regId,
                [
                    new OperationalRegisterDimensionRule(Guid.CreateVersion7(), "building", 10, IsRequired: false),
                    new OperationalRegisterDimensionRule(Guid.CreateVersion7(), "unit", 10, IsRequired: false)
                ],
                nowUtc,
                CancellationToken.None);

            var ex = await act.Should().ThrowAsync<OperationalRegisterDimensionRulesValidationException>();
            ex.Which.ErrorCode.Should().Be(OperationalRegisterDimensionRulesValidationException.Code);
            ex.Which.Reason.Should().Be("duplicate_ordinal");
            ex.Which.Context.Should().ContainKey("collisions");
            ((string[])ex.Which.Context["collisions"]!).Single().Should().StartWith("10:");
        }
        finally
        {
            if (uow.HasActiveTransaction)
                await uow.RollbackAsync(CancellationToken.None);
        }
    }
}
