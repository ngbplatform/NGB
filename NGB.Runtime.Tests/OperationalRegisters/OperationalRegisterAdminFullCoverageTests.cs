using FluentAssertions;
using Moq;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.OperationalRegisters;

public sealed class OperationalRegisterAdminFullCoverageTests
{
    private static readonly DateTime Now = new(2026, 8, 15, 16, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ReadService_DelegatesEveryReadOperation()
    {
        var id = Guid.NewGuid();
        var month = new DateOnly(2026, 1, 1);
        var reader = new Mock<IOperationalRegisterAdminReader>(MockBehavior.Loose);
        var health = new Mock<IOperationalRegisterPhysicalSchemaHealthReader>(MockBehavior.Loose);
        var finalizations = new Mock<IOperationalRegisterFinalizationRepository>(MockBehavior.Loose);
        var sut = new OperationalRegisterAdminReadService(reader.Object, health.Object, finalizations.Object);

        await sut.GetListAsync();
        await sut.GetDetailsByIdAsync(id);
        await sut.GetDetailsByCodeAsync("stock");
        await sut.GetPhysicalSchemaHealthReportAsync();
        await sut.GetPhysicalSchemaHealthByIdAsync(id);
        await sut.GetFinalizationAsync(id, month);
        await sut.GetDirtyFinalizationsAsync(id, 1);
        await sut.GetBlockedFinalizationsAsync(id, 2);
        await sut.GetDirtyFinalizationsAcrossAllAsync(3);
        await sut.GetBlockedFinalizationsAcrossAllAsync(4);

        reader.Verify(x => x.GetListAsync(It.IsAny<CancellationToken>()), Times.Once);
        reader.Verify(x => x.GetDetailsByIdAsync(id, It.IsAny<CancellationToken>()), Times.Once);
        reader.Verify(x => x.GetDetailsByCodeAsync("stock", It.IsAny<CancellationToken>()), Times.Once);
        health.Verify(x => x.GetReportAsync(It.IsAny<CancellationToken>()), Times.Once);
        health.Verify(x => x.GetByRegisterIdAsync(id, It.IsAny<CancellationToken>()), Times.Once);
        finalizations.Verify(x => x.GetAsync(id, month, It.IsAny<CancellationToken>()), Times.Once);
        finalizations.Verify(x => x.GetDirtyAsync(id, 1, It.IsAny<CancellationToken>()), Times.Once);
        finalizations.Verify(x => x.GetBlockedAsync(id, 2, It.IsAny<CancellationToken>()), Times.Once);
        finalizations.Verify(x => x.GetDirtyAcrossAllAsync(3, It.IsAny<CancellationToken>()), Times.Once);
        finalizations.Verify(x => x.GetBlockedAcrossAllAsync(4, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Endpoint_ValidatesAllRequiredIdsCodesPeriodsAndLimits()
    {
        var f = new EndpointFixture();
        var id = Guid.NewGuid();
        var month = new DateOnly(2026, 1, 1);

        await ((Func<Task>)(() => f.Sut.GetDetailsByIdAsync(Guid.Empty))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.GetDetailsByCodeAsync(" "))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.GetPhysicalSchemaHealthByIdAsync(Guid.Empty))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.EnsurePhysicalSchemaByIdAsync(Guid.Empty))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.GetFinalizationAsync(Guid.Empty, month))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.GetFinalizationAsync(id, month.AddDays(1)))).Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => f.Sut.GetDirtyFinalizationsByIdAsync(Guid.Empty))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.GetDirtyFinalizationsByIdAsync(id, 0))).Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => f.Sut.GetBlockedFinalizationsByIdAsync(Guid.Empty))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.GetBlockedFinalizationsByIdAsync(id, -1))).Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => f.Sut.GetDirtyFinalizationsAcrossAllAsync(0))).Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => f.Sut.GetBlockedFinalizationsAcrossAllAsync(-1))).Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => f.Sut.MarkFinalizationDirtyAsync(Guid.Empty, month))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.MarkFinalizationDirtyAsync(id, month.AddDays(1)))).Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => f.Sut.FinalizeDirtyAsync(0))).Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => f.Sut.FinalizeRegisterDirtyAsync(Guid.Empty))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.FinalizeRegisterDirtyAsync(id, 0))).Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Endpoint_CoversEmptyNullAndMappedResultsAcrossEveryOperation()
    {
        var f = new EndpointFixture();
        var id = Guid.NewGuid();
        var month = new DateOnly(2026, 2, 1);
        var register = Register(id);
        var listItem = new OperationalRegisterAdminListItem(register, 2, 3);
        var details = new OperationalRegisterAdminDetails(register,
            [new OperationalRegisterResource("amount", "amount", "amount", "Amount", 1)],
            [new OperationalRegisterDimensionRule(Guid.NewGuid(), "department", 2, true)]);
        var report = HealthReport(register);
        var marker = Finalization(id, month);

        f.Read.SetupSequence(x => x.GetListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]).ReturnsAsync([listItem]);
        (await f.Sut.GetListAsync()).Should().BeEmpty();
        var list = await f.Sut.GetListAsync();
        list.Should().ContainSingle();
        list[0].Register.Should().BeEquivalentTo(register);
        list[0].ResourcesCount.Should().Be(2);
        list[0].DimensionRulesCount.Should().Be(3);

        f.Read.SetupSequence(x => x.GetDetailsByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationalRegisterAdminDetails?)null).ReturnsAsync(details);
        (await f.Sut.GetDetailsByIdAsync(id)).Should().BeNull();
        var idDetails = await f.Sut.GetDetailsByIdAsync(id);
        idDetails!.Resources.Should().ContainSingle().Which.ColumnCode.Should().Be("amount");
        idDetails.DimensionRules.Should().ContainSingle().Which.IsRequired.Should().BeTrue();
        idDetails.Register.RegisterId.Should().Be(id);
        var resourceDto = idDetails.Resources[0];
        resourceDto.Code.Should().Be("amount");
        resourceDto.CodeNorm.Should().Be("amount");
        resourceDto.Name.Should().Be("Amount");
        resourceDto.Ordinal.Should().Be(1);
        var ruleDto = idDetails.DimensionRules[0];
        ruleDto.DimensionId.Should().NotBeEmpty();
        ruleDto.DimensionCode.Should().Be("department");
        ruleDto.Ordinal.Should().Be(2);

        f.Read.SetupSequence(x => x.GetDetailsByCodeAsync("stock", It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationalRegisterAdminDetails?)null).ReturnsAsync(details);
        (await f.Sut.GetDetailsByCodeAsync("stock")).Should().BeNull();
        (await f.Sut.GetDetailsByCodeAsync("stock")).Should().NotBeNull();

        f.Read.Setup(x => x.GetPhysicalSchemaHealthReportAsync(It.IsAny<CancellationToken>())).ReturnsAsync(report);
        var reportDto = await f.Sut.GetPhysicalSchemaHealthReportAsync();
        reportDto.TotalCount.Should().Be(1);
        reportDto.OkCount.Should().Be(0);
        reportDto.Items[0].Movements.MissingColumns.Should().Equal("missing");
        reportDto.Items[0].Movements.HasAppendOnlyGuard.Should().BeFalse();
        reportDto.Items[0].Register.RegisterId.Should().Be(id);
        reportDto.Items[0].Movements.TableName.Should().Be("movements");
        reportDto.Items[0].Movements.Exists.Should().BeTrue();
        reportDto.Items[0].Movements.MissingIndexes.Should().BeEmpty();
        reportDto.Items[0].Turnovers.IsOk.Should().BeTrue();
        reportDto.Items[0].Balances.IsOk.Should().BeTrue();

        f.Read.SetupSequence(x => x.GetPhysicalSchemaHealthByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationalRegisterPhysicalSchemaHealth?)null).ReturnsAsync(report.Items[0]);
        (await f.Sut.GetPhysicalSchemaHealthByIdAsync(id)).Should().BeNull();
        (await f.Sut.GetPhysicalSchemaHealthByIdAsync(id))!.IsOk.Should().BeFalse();

        f.Maintenance.SetupSequence(x => x.EnsurePhysicalSchemaByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationalRegisterPhysicalSchemaHealth?)null).ReturnsAsync(report.Items[0]);
        (await f.Sut.EnsurePhysicalSchemaByIdAsync(id)).Should().BeNull();
        (await f.Sut.EnsurePhysicalSchemaByIdAsync(id)).Should().NotBeNull();
        f.Maintenance.Setup(x => x.EnsurePhysicalSchemaForAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(report);
        (await f.Sut.EnsurePhysicalSchemaForAllAsync()).Items.Should().ContainSingle();

        f.Read.SetupSequence(x => x.GetFinalizationAsync(id, month, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationalRegisterFinalization?)null).ReturnsAsync(marker);
        (await f.Sut.GetFinalizationAsync(id, month)).Should().BeNull();
        var markerDto = await f.Sut.GetFinalizationAsync(id, month);
        markerDto!.Status.Should().Be("BlockedNoProjector");
        markerDto.BlockedReason.Should().Be("missing");
        markerDto.Period.Should().Be(month);
        markerDto.FinalizedAtUtc.Should().Be(Now);
        markerDto.DirtySinceUtc.Should().Be(Now);
        markerDto.BlockedSinceUtc.Should().Be(Now);
        markerDto.CreatedAtUtc.Should().Be(Now);
        markerDto.UpdatedAtUtc.Should().Be(Now);

        await AssertRegisterListVariants(
            () => f.Sut.GetDirtyFinalizationsByIdAsync(id, 1),
            setup => f.Read.SetupSequence(x => x.GetDirtyFinalizationsAsync(id, 1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(setup[0]).ReturnsAsync(setup[1]), marker);
        await AssertRegisterListVariants(
            () => f.Sut.GetBlockedFinalizationsByIdAsync(id, 2),
            setup => f.Read.SetupSequence(x => x.GetBlockedFinalizationsAsync(id, 2, It.IsAny<CancellationToken>()))
                .ReturnsAsync(setup[0]).ReturnsAsync(setup[1]), marker);
        await AssertRegisterListVariants(
            () => f.Sut.GetDirtyFinalizationsAcrossAllAsync(3),
            setup => f.Read.SetupSequence(x => x.GetDirtyFinalizationsAcrossAllAsync(3, It.IsAny<CancellationToken>()))
                .ReturnsAsync(setup[0]).ReturnsAsync(setup[1]), marker);
        await AssertRegisterListVariants(
            () => f.Sut.GetBlockedFinalizationsAcrossAllAsync(4),
            setup => f.Read.SetupSequence(x => x.GetBlockedFinalizationsAcrossAllAsync(4, It.IsAny<CancellationToken>()))
                .ReturnsAsync(setup[0]).ReturnsAsync(setup[1]), marker);

        await f.Sut.MarkFinalizationDirtyAsync(id, month);
        f.Maintenance.Setup(x => x.FinalizeDirtyAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(4);
        (await f.Sut.FinalizeDirtyAsync(5)).Should().Be(4);
        f.Maintenance.Setup(x => x.FinalizeRegisterDirtyAsync(id, 6, It.IsAny<CancellationToken>())).ReturnsAsync(3);
        (await f.Sut.FinalizeRegisterDirtyAsync(id, 6)).Should().Be(3);
    }

    [Fact]
    public async Task Maintenance_CoversValidationMissingEmptyMultipleAndDelegatedFinalization()
    {
        var f = new MaintenanceFixture();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var month = new DateOnly(2026, 3, 1);

        await ((Func<Task>)(() => f.Sut.EnsurePhysicalSchemaByIdAsync(Guid.Empty)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        f.Registers.Setup(x => x.GetByIdAsync(first, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationalRegisterAdminItem?)null);
        (await f.Sut.EnsurePhysicalSchemaByIdAsync(first)).Should().BeNull();

        var firstRegister = Register(first);
        var firstHealth = HealthReport(firstRegister).Items[0];
        f.Registers.Setup(x => x.GetByIdAsync(first, It.IsAny<CancellationToken>())).ReturnsAsync(firstRegister);
        f.Health.Setup(x => x.GetByRegisterIdAsync(first, It.IsAny<CancellationToken>())).ReturnsAsync(firstHealth);
        (await f.Sut.EnsurePhysicalSchemaByIdAsync(first)).Should().BeSameAs(firstHealth);
        f.Movements.Verify(x => x.EnsureSchemaAsync(first, It.IsAny<CancellationToken>()), Times.Once);
        f.Turnovers.Verify(x => x.EnsureSchemaAsync(first, It.IsAny<CancellationToken>()), Times.Once);
        f.Balances.Verify(x => x.EnsureSchemaAsync(first, It.IsAny<CancellationToken>()), Times.Once);

        var report = HealthReport(firstRegister);
        f.Health.Setup(x => x.GetReportAsync(It.IsAny<CancellationToken>())).ReturnsAsync(report);
        f.Registers.SetupSequence(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([firstRegister, Register(second)]);
        (await f.Sut.EnsurePhysicalSchemaForAllAsync()).Should().BeSameAs(report);
        (await f.Sut.EnsurePhysicalSchemaForAllAsync()).Should().BeSameAs(report);
        f.Movements.Verify(x => x.EnsureSchemaAsync(second, It.IsAny<CancellationToken>()), Times.Once);

        await ((Func<Task>)(() => f.Sut.MarkFinalizationDirtyAsync(Guid.Empty, month)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => f.Sut.MarkFinalizationDirtyAsync(first, month.AddDays(1))))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await f.Sut.MarkFinalizationDirtyAsync(first, month);
        f.Finalizations.Verify(x => x.MarkDirtyAsync(first, month, true, It.IsAny<CancellationToken>()), Times.Once);

        f.Runner.Setup(x => x.FinalizeDirtyAsync(7, true, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        (await f.Sut.FinalizeDirtyAsync(7)).Should().Be(2);
        f.Runner.Setup(x => x.FinalizeRegisterDirtyAsync(first, 8, true, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        (await f.Sut.FinalizeRegisterDirtyAsync(first, 8)).Should().Be(1);
    }

    private static async Task AssertRegisterListVariants(
        Func<Task<IReadOnlyList<OperationalRegisterAdminEndpointContracts.FinalizationDto>>> action,
        Action<IReadOnlyList<OperationalRegisterFinalization>[]> setup,
        OperationalRegisterFinalization marker)
    {
        setup([[], [marker]]);
        (await action()).Should().BeEmpty();
        (await action()).Should().ContainSingle().Which.RegisterId.Should().Be(marker.RegisterId);
    }

    private sealed class EndpointFixture
    {
        public Mock<IOperationalRegisterAdminReadService> Read { get; } = new(MockBehavior.Loose);
        public Mock<IOperationalRegisterAdminMaintenanceService> Maintenance { get; } = new(MockBehavior.Loose);
        public OperationalRegisterAdminEndpoint Sut { get; }
        public EndpointFixture() => Sut = new(Read.Object, Maintenance.Object);
    }

    private sealed class MaintenanceFixture
    {
        public Mock<IUnitOfWork> Uow { get; } = new(MockBehavior.Loose);
        public Mock<IOperationalRegisterRepository> Registers { get; } = new(MockBehavior.Loose);
        public Mock<IOperationalRegisterMovementsStore> Movements { get; } = new(MockBehavior.Loose);
        public Mock<IOperationalRegisterTurnoversStore> Turnovers { get; } = new(MockBehavior.Loose);
        public Mock<IOperationalRegisterBalancesStore> Balances { get; } = new(MockBehavior.Loose);
        public Mock<IOperationalRegisterPhysicalSchemaHealthReader> Health { get; } = new(MockBehavior.Loose);
        public Mock<IOperationalRegisterFinalizationService> Finalizations { get; } = new(MockBehavior.Loose);
        public Mock<IOperationalRegisterFinalizationRunner> Runner { get; } = new(MockBehavior.Loose);
        public OperationalRegisterAdminMaintenanceService Sut { get; }

        public MaintenanceFixture() => Sut = new(Uow.Object, Registers.Object, Movements.Object,
            Turnovers.Object, Balances.Object, Health.Object, Finalizations.Object, Runner.Object);
    }

    private static OperationalRegisterAdminItem Register(Guid id)
        => new(id, "stock", "stock", "stock", "Stock", true, Now, Now);

    private static OperationalRegisterPhysicalSchemaHealthReport HealthReport(OperationalRegisterAdminItem register)
        => new([
            new OperationalRegisterPhysicalSchemaHealth(
                register,
                new OperationalRegisterPhysicalTableHealth("movements", true, ["missing"], [], false),
                new OperationalRegisterPhysicalTableHealth("turnovers", true, [], [], null),
                new OperationalRegisterPhysicalTableHealth("balances", true, [], [], true))
        ]);

    private static OperationalRegisterFinalization Finalization(Guid id, DateOnly month)
        => new(id, month, OperationalRegisterFinalizationStatus.BlockedNoProjector,
            Now, Now, Now, "missing", Now, Now);
}
