using FluentAssertions;
using Moq;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.PostgreSql.OperationalRegisters;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.OperationalRegisters;

public sealed class PostgresOperationalRegisterDefaultProjectionRebuilderFullCoverageTests
{
    private static readonly Guid RegisterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly Period = new(2026, 8, 1);

    [Fact]
    public async Task Rebuild_validates_register_periods_and_active_transaction()
    {
        var inactive = Fixture(hasActiveTransaction: false).Sut;
        await ((Func<Task>)(() => inactive.RebuildMonthAsync(Guid.Empty, Period, null, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => inactive.RebuildMonthAsync(RegisterId, Period.AddDays(1), null, default)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => inactive.RebuildMonthAsync(RegisterId, Period, Period.AddDays(1), default)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => inactive.RebuildMonthAsync(RegisterId, Period, null, default)))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Rebuild_aggregates_resources_and_rolls_forward_previous_balances_in_one_statement()
    {
        var fixture = Fixture(
            movementsExist: true,
            resources:
            [
                new("amount", "amount", "amount", "Amount", 2),
                new("quantity", "quantity", "quantity", "Quantity", 1)
            ]);

        await fixture.Sut.RebuildMonthAsync(RegisterId, Period, Period.AddMonths(-1), default);

        var sql = fixture.Connection.Commands.Last().CommandText;
        sql.Should().Contain("SUM(CASE WHEN is_storno THEN -quantity ELSE quantity END)")
            .And.Contain("SUM(CASE WHEN is_storno THEN -amount ELSE amount END)")
            .And.Contain("FULL JOIN")
            .And.Contain("period_month = @PreviousPeriod")
            .And.Contain("COALESCE(p.quantity, 0::numeric) + COALESCE(t.quantity, 0::numeric)");
        fixture.Turnovers.Verify(x => x.EnsureReadyForWriteAsync(RegisterId, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Balances.Verify(x => x.EnsureReadyForWriteAsync(RegisterId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rebuild_handles_zero_resource_registers_and_missing_previous_snapshot(bool movementsExist)
    {
        var fixture = Fixture(movementsExist: movementsExist, resources: []);

        await fixture.Sut.RebuildMonthAsync(RegisterId, Period, null, default);

        var sql = fixture.Connection.Commands.Last().CommandText;
        sql.Should().Contain("DELETE FROM opreg_sales__turnovers")
            .And.Contain("DELETE FROM opreg_sales__balances")
            .And.Contain("SELECT NULL::uuid AS dimension_set_id WHERE FALSE")
            .And.NotContain("numeric");
        if (movementsExist)
            sql.Should().Contain("FROM opreg_sales__movements");
        else
            sql.Should().NotContain("FROM opreg_sales__movements");
    }

    [Fact]
    public async Task Rebuild_covers_previous_source_boundaries_and_missing_register()
    {
        var zeroResources = Fixture(movementsExist: false, resources: []);
        await zeroResources.Sut.RebuildMonthAsync(RegisterId, Period, Period.AddMonths(-1), default);
        zeroResources.Connection.Commands.Last().CommandText.Should()
            .Contain("SELECT dimension_set_id FROM opreg_sales__balances WHERE period_month = @PreviousPeriod");

        var firstMonthWithResources = Fixture(
            movementsExist: false,
            resources: [new("amount", "amount", "amount", "Amount", 1)]);
        await firstMonthWithResources.Sut.RebuildMonthAsync(RegisterId, Period, null, default);
        firstMonthWithResources.Connection.Commands.Last().CommandText.Should()
            .Contain("0::numeric AS amount")
            .And.Contain("WHERE FALSE");

        var missing = Fixture(registerExists: false).Sut;
        await ((Func<Task>)(() => missing.RebuildMonthAsync(RegisterId, Period, null, default)))
            .Should().ThrowAsync<NGB.OperationalRegisters.Exceptions.OperationalRegisterNotFoundException>();
    }

    private static FixtureState Fixture(
        bool hasActiveTransaction = true,
        bool movementsExist = true,
        IReadOnlyList<OperationalRegisterResource>? resources = null,
        bool registerExists = true)
    {
        var connection = new RecordingDbConnection(scalar: _ => movementsExist);
        var registers = new Mock<IOperationalRegisterRepository>(MockBehavior.Strict);
        OperationalRegisterAdminItem? register = registerExists
            ? new OperationalRegisterAdminItem(
                RegisterId,
                "Sales",
                "sales",
                "sales",
                "Sales",
                true,
                DateTime.UnixEpoch,
                DateTime.UnixEpoch)
            : null;
        registers.Setup(x => x.GetByIdAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(register);
        var resourceRepository = new Mock<IOperationalRegisterResourceRepository>(MockBehavior.Strict);
        resourceRepository.Setup(x => x.GetByRegisterIdAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resources ?? []);
        var turnovers = new Mock<IOperationalRegisterTurnoversStore>(MockBehavior.Strict);
        turnovers.Setup(x => x.EnsureReadyForWriteAsync(RegisterId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var balances = new Mock<IOperationalRegisterBalancesStore>(MockBehavior.Strict);
        balances.Setup(x => x.EnsureReadyForWriteAsync(RegisterId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var sut = new PostgresOperationalRegisterDefaultProjectionRebuilder(
            new RecordingUnitOfWork(connection, hasActiveTransaction),
            registers.Object,
            resourceRepository.Object,
            turnovers.Object,
            balances.Object);
        return new FixtureState(sut, connection, turnovers, balances);
    }

    private sealed record FixtureState(
        PostgresOperationalRegisterDefaultProjectionRebuilder Sut,
        RecordingDbConnection Connection,
        Mock<IOperationalRegisterTurnoversStore> Turnovers,
        Mock<IOperationalRegisterBalancesStore> Balances);
}
