using FluentAssertions;
using Moq;
using NGB.Metadata.Base;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.ReferenceRegisters;

public sealed class ReferenceRegisterAdminFullCoverageTests
{
    private static readonly DateTime Now = new(2026, 8, 15, 19, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ReadService_CoversEmptyListCountsNullDetailsAndHealthDelegation()
    {
        var id = Guid.NewGuid();
        var register = Register(id);
        var registers = new Mock<IReferenceRegisterRepository>(MockBehavior.Loose);
        var fields = new Mock<IReferenceRegisterFieldRepository>(MockBehavior.Loose);
        var dimensions = new Mock<IReferenceRegisterDimensionRuleRepository>(MockBehavior.Loose);
        var health = new Mock<IReferenceRegisterPhysicalSchemaHealthReader>(MockBehavior.Loose);
        var sut = new ReferenceRegisterAdminReadService(registers.Object, fields.Object, dimensions.Object, health.Object);

        registers.SetupSequence(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]).ReturnsAsync([register]);
        (await sut.GetListAsync()).Should().BeEmpty();
        fields.Setup(x => x.GetByRegisterIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync([Field(id)]);
        dimensions.Setup(x => x.GetByRegisterIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync([
            new ReferenceRegisterDimensionRule(Guid.NewGuid(), "department", 1, true),
            new ReferenceRegisterDimensionRule(Guid.NewGuid(), "project", 2, false)
        ]);
        var list = await sut.GetListAsync();
        list.Should().ContainSingle();
        list[0].FieldsCount.Should().Be(1);
        list[0].DimensionRulesCount.Should().Be(2);

        registers.SetupSequence(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReferenceRegisterAdminItem?)null).ReturnsAsync(register);
        (await sut.GetDetailsByIdAsync(id)).Should().BeNull();
        (await sut.GetDetailsByIdAsync(id))!.Fields.Should().ContainSingle();

        registers.SetupSequence(x => x.GetByCodeAsync("prices", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReferenceRegisterAdminItem?)null).ReturnsAsync(register);
        (await sut.GetDetailsByCodeAsync("prices")).Should().BeNull();
        (await sut.GetDetailsByCodeAsync("prices")).Should().NotBeNull();

        await sut.GetPhysicalSchemaHealthReportAsync();
        await sut.GetPhysicalSchemaHealthByIdAsync(id);
        health.Verify(x => x.GetReportAsync(It.IsAny<CancellationToken>()), Times.Once);
        health.Verify(x => x.GetByRegisterIdAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Maintenance_ValidatesMissingEnsuresOneEmptyAndAllRegisters()
    {
        var id = Guid.NewGuid();
        var second = Guid.NewGuid();
        var register = Register(id);
        var uow = new Mock<IUnitOfWork>(MockBehavior.Loose);
        var registers = new Mock<IReferenceRegisterRepository>(MockBehavior.Loose);
        var store = new Mock<IReferenceRegisterRecordsStore>(MockBehavior.Loose);
        var health = new Mock<IReferenceRegisterPhysicalSchemaHealthReader>(MockBehavior.Loose);
        var sut = new ReferenceRegisterAdminMaintenanceService(uow.Object, registers.Object, store.Object, health.Object);

        await ((Func<Task>)(() => sut.EnsurePhysicalSchemaByIdAsync(Guid.Empty)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        registers.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReferenceRegisterAdminItem?)null);
        (await sut.EnsurePhysicalSchemaByIdAsync(id)).Should().BeNull();

        var itemHealth = Health(register);
        registers.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(register);
        health.Setup(x => x.GetByRegisterIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(itemHealth);
        (await sut.EnsurePhysicalSchemaByIdAsync(id)).Should().BeSameAs(itemHealth);
        store.Verify(x => x.EnsureSchemaAsync(id, It.IsAny<CancellationToken>()), Times.Once);

        registers.SetupSequence(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]).ReturnsAsync([register, Register(second)]);
        (await sut.EnsurePhysicalSchemaForAllAsync()).Items.Should().BeEmpty();
        var report = new ReferenceRegisterPhysicalSchemaHealthReport([itemHealth]);
        health.Setup(x => x.GetReportAsync(It.IsAny<CancellationToken>())).ReturnsAsync(report);
        (await sut.EnsurePhysicalSchemaForAllAsync()).Should().BeSameAs(report);
        store.Verify(x => x.EnsureSchemaAsync(id, It.IsAny<CancellationToken>()), Times.Exactly(2));
        store.Verify(x => x.EnsureSchemaAsync(second, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Endpoint_ValidatesInputsAndCoversEmptyNullAndMappedResponses()
    {
        var id = Guid.NewGuid();
        var register = Register(id);
        var field = Field(id);
        var rule = new ReferenceRegisterDimensionRule(Guid.NewGuid(), "department", 2, true);
        var details = new ReferenceRegisterAdminDetails(register, [field], [rule]);
        var itemHealth = Health(register);
        var report = new ReferenceRegisterPhysicalSchemaHealthReport([itemHealth]);
        var read = new Mock<IReferenceRegisterAdminReadService>(MockBehavior.Loose);
        var maintenance = new Mock<IReferenceRegisterAdminMaintenanceService>(MockBehavior.Loose);
        var sut = new ReferenceRegisterAdminEndpoint(read.Object, maintenance.Object);

        await ((Func<Task>)(() => sut.GetDetailsByIdAsync(Guid.Empty))).Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => sut.GetDetailsByCodeAsync(" "))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => sut.GetPhysicalSchemaHealthByIdAsync(Guid.Empty))).Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => sut.EnsurePhysicalSchemaByIdAsync(Guid.Empty))).Should().ThrowAsync<NgbArgumentOutOfRangeException>();

        read.SetupSequence(x => x.GetListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([(register, 1, 2)]);
        (await sut.GetListAsync()).Should().BeEmpty();
        var list = await sut.GetListAsync();
        list.Should().ContainSingle();
        list[0].FieldsCount.Should().Be(1);
        list[0].DimensionRulesCount.Should().Be(2);
        AssertRegisterDto(list[0].Register, id);

        read.SetupSequence(x => x.GetDetailsByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReferenceRegisterAdminDetails?)null).ReturnsAsync(details);
        (await sut.GetDetailsByIdAsync(id)).Should().BeNull();
        var byId = await sut.GetDetailsByIdAsync(id);
        AssertDetailsDto(byId!, id);

        read.SetupSequence(x => x.GetDetailsByCodeAsync("prices", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReferenceRegisterAdminDetails?)null).ReturnsAsync(details);
        (await sut.GetDetailsByCodeAsync("prices")).Should().BeNull();
        AssertDetailsDto((await sut.GetDetailsByCodeAsync("prices"))!, id);

        read.Setup(x => x.GetPhysicalSchemaHealthReportAsync(It.IsAny<CancellationToken>())).ReturnsAsync(report);
        var reportDto = await sut.GetPhysicalSchemaHealthReportAsync();
        reportDto.TotalCount.Should().Be(1);
        reportDto.OkCount.Should().Be(0);
        reportDto.Items.Should().ContainSingle();
        AssertHealthDto(reportDto.Items[0], id);

        read.SetupSequence(x => x.GetPhysicalSchemaHealthByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReferenceRegisterPhysicalSchemaHealth?)null).ReturnsAsync(itemHealth);
        (await sut.GetPhysicalSchemaHealthByIdAsync(id)).Should().BeNull();
        AssertHealthDto((await sut.GetPhysicalSchemaHealthByIdAsync(id))!, id);

        maintenance.SetupSequence(x => x.EnsurePhysicalSchemaByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReferenceRegisterPhysicalSchemaHealth?)null).ReturnsAsync(itemHealth);
        (await sut.EnsurePhysicalSchemaByIdAsync(id)).Should().BeNull();
        AssertHealthDto((await sut.EnsurePhysicalSchemaByIdAsync(id))!, id);
        maintenance.Setup(x => x.EnsurePhysicalSchemaForAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(report);
        (await sut.EnsurePhysicalSchemaForAllAsync()).Items.Should().ContainSingle();
    }

    private static void AssertRegisterDto(ReferenceRegisterAdminEndpointContracts.RegisterDto dto, Guid id)
    {
        dto.RegisterId.Should().Be(id);
        dto.Code.Should().Be("prices");
        dto.CodeNorm.Should().Be("prices");
        dto.TableCode.Should().Be("prices");
        dto.Name.Should().Be("Prices");
        dto.Periodicity.Should().Be(ReferenceRegisterPeriodicity.Month);
        dto.RecordMode.Should().Be(ReferenceRegisterRecordMode.SubordinateToRecorder);
        dto.HasRecords.Should().BeTrue();
        dto.CreatedAtUtc.Should().Be(Now);
        dto.UpdatedAtUtc.Should().Be(Now);
    }

    private static void AssertDetailsDto(ReferenceRegisterAdminEndpointContracts.RegisterDetailsDto dto, Guid id)
    {
        AssertRegisterDto(dto.Register, id);
        dto.Fields.Should().ContainSingle();
        var field = dto.Fields[0];
        field.Code.Should().Be("amount");
        field.CodeNorm.Should().Be("amount");
        field.ColumnCode.Should().Be("amount");
        field.Name.Should().Be("Amount");
        field.Ordinal.Should().Be(1);
        field.ColumnType.Should().Be(ColumnType.Decimal);
        field.IsNullable.Should().BeFalse();
        dto.DimensionRules.Should().ContainSingle();
        var rule = dto.DimensionRules[0];
        rule.DimensionId.Should().NotBeEmpty();
        rule.DimensionCode.Should().Be("department");
        rule.Ordinal.Should().Be(2);
        rule.IsRequired.Should().BeTrue();
    }

    private static void AssertHealthDto(ReferenceRegisterAdminEndpointContracts.PhysicalSchemaHealthDto dto, Guid id)
    {
        AssertRegisterDto(dto.Register, id);
        dto.IsOk.Should().BeFalse();
        dto.Records.TableName.Should().Be("records");
        dto.Records.Exists.Should().BeTrue();
        dto.Records.MissingColumns.Should().Equal("missing");
        dto.Records.MissingIndexes.Should().BeEmpty();
        dto.Records.HasAppendOnlyGuard.Should().BeFalse();
        dto.Records.IsOk.Should().BeFalse();
    }

    private static ReferenceRegisterAdminItem Register(Guid id)
        => new(id, "prices", "prices", "prices", "Prices", ReferenceRegisterPeriodicity.Month,
            ReferenceRegisterRecordMode.SubordinateToRecorder, true, Now, Now);

    private static ReferenceRegisterField Field(Guid id)
        => new(id, "amount", "amount", "amount", "Amount", 1, ColumnType.Decimal, false, Now, Now);

    private static ReferenceRegisterPhysicalSchemaHealth Health(ReferenceRegisterAdminItem register)
        => new(register, new ReferenceRegisterPhysicalTableHealth("records", true, ["missing"], [], false));
}
