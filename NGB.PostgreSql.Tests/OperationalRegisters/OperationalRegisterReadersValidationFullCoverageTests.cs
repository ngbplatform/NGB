using System.Data;
using FluentAssertions;
using Moq;
using NGB.Core.Dimensions;
using NGB.Core.Dimensions.Enrichment;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.PostgreSql.OperationalRegisters;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.OperationalRegisters;

public sealed class OperationalRegisterReadersValidationFullCoverageTests
{
    private static readonly Guid RegisterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DimensionSetId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Movements_reader_rejects_empty_identifiers_and_non_positive_limits()
    {
        var sut = new PostgresOperationalRegisterMovementsReader(null!, null!, null!);

        Func<Task> emptyRegister = () => sut.GetByMonthAsync(Guid.Empty, DateOnly.MinValue);
        Func<Task> zeroLimit = () => sut.GetByMonthAsync(RegisterId, DateOnly.MaxValue, limit: 0);
        Func<Task> negativeLimit = () => sut.GetByMonthAsync(RegisterId, DateOnly.MaxValue, limit: -1);
        Func<Task> distinctEmptyRegister = () => sut.GetDistinctMonthsByDocumentAsync(Guid.Empty, Guid.NewGuid());
        Func<Task> distinctEmptyDocument = () => sut.GetDistinctMonthsByDocumentAsync(RegisterId, Guid.Empty);

        await emptyRegister.Should().ThrowAsync<NgbArgumentRequiredException>();
        await zeroLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await negativeLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await distinctEmptyRegister.Should().ThrowAsync<NgbArgumentRequiredException>();
        await distinctEmptyDocument.Should().ThrowAsync<NgbArgumentRequiredException>();
    }

    [Fact]
    public async Task Resource_net_reader_validates_both_query_shapes_before_database_access()
    {
        var sut = new PostgresOperationalRegisterResourceNetReader(null!, null!, null!);

        Func<Task> emptyRegister = () => sut.GetNetByDimensionSetAsync(Guid.Empty, DimensionSetId, "amount");
        Func<Task> emptySet = () => sut.GetNetByDimensionSetAsync(RegisterId, Guid.Empty, "amount");
        Func<Task> blankSetResource = () => sut.GetNetByDimensionSetAsync(RegisterId, DimensionSetId, " ");
        Func<Task> emptyDimensionsRegister = () => sut.GetNetByDimensionsAsync(Guid.Empty, [], "amount");
        Func<Task> nullDimensions = () => sut.GetNetByDimensionsAsync(RegisterId, null!, "amount");
        Func<Task> emptyDimensions = () => sut.GetNetByDimensionsAsync(RegisterId, [], "amount");
        Func<Task> blankDimensionsResource = () => sut.GetNetByDimensionsAsync(
            RegisterId, [new DimensionValue(Guid.NewGuid(), Guid.NewGuid())], "\t");

        await emptyRegister.Should().ThrowAsync<NgbArgumentInvalidException>();
        await emptySet.Should().ThrowAsync<NgbArgumentInvalidException>();
        await blankSetResource.Should().ThrowAsync<NgbArgumentRequiredException>();
        await emptyDimensionsRegister.Should().ThrowAsync<NgbArgumentInvalidException>();
        await nullDimensions.Should().ThrowAsync<ArgumentNullException>();
        await emptyDimensions.Should().ThrowAsync<NgbArgumentInvalidException>();
        await blankDimensionsResource.Should().ThrowAsync<NgbArgumentRequiredException>();
    }

    [Fact]
    public async Task Resource_net_reader_rejects_a_resource_not_declared_by_the_register_for_both_query_shapes()
    {
        var registers = new Mock<IOperationalRegisterRepository>(MockBehavior.Strict);
        registers.Setup(x => x.GetByIdAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register());
        var resources = new Mock<IOperationalRegisterResourceRepository>(MockBehavior.Strict);
        resources.Setup(x => x.GetByRegisterIdAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new("Quantity", "quantity", "quantity", "Quantity", 1)]);
        var sut = new PostgresOperationalRegisterResourceNetReader(
            new RecordingUnitOfWork(new RecordingDbConnection(), hasActiveTransaction: true),
            registers.Object,
            resources.Object);

        Func<Task> bySet = () => sut.GetNetByDimensionSetAsync(RegisterId, DimensionSetId, "amount");
        Func<Task> byDimensions = () => sut.GetNetByDimensionsAsync(
            RegisterId, [new DimensionValue(Guid.NewGuid(), Guid.NewGuid())], "amount");

        await bySet.Should().ThrowAsync<NgbConfigurationViolationException>();
        await byDimensions.Should().ThrowAsync<NgbConfigurationViolationException>();
        registers.VerifyAll();
        resources.VerifyAll();
    }

    [Fact]
    public async Task Resource_net_reader_returns_zero_when_the_physical_table_does_not_exist_for_both_query_shapes()
    {
        var dependencies = RegisterDependencies([new("Amount", "amount", "amount", "Amount", 1)]);
        var sut = new PostgresOperationalRegisterResourceNetReader(
            new RecordingUnitOfWork(
                new RecordingDbConnection(scalar: _ => false),
                hasActiveTransaction: true),
            dependencies.Registers.Object,
            dependencies.Resources.Object);

        (await sut.GetNetByDimensionSetAsync(RegisterId, DimensionSetId, "amount")).Should().Be(0m);
        (await sut.GetNetByDimensionsAsync(
            RegisterId,
            [new DimensionValue(Guid.NewGuid(), Guid.NewGuid())],
            "amount")).Should().Be(0m);

        dependencies.Registers.Verify(x => x.GetByIdAsync(RegisterId, It.IsAny<CancellationToken>()), Times.Exactly(2));
        dependencies.Resources.Verify(x => x.GetByRegisterIdAsync(RegisterId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Resource_net_reader_returns_database_net_when_the_physical_table_exists()
    {
        var dependencies = RegisterDependencies([new("Amount", "amount", "amount", "Amount", 1)]);
        var connection = new RecordingDbConnection(scalar: sql =>
            sql.Contains("to_regclass", StringComparison.Ordinal) ? true : 12.5m);
        var sut = new PostgresOperationalRegisterResourceNetReader(
            new RecordingUnitOfWork(connection, hasActiveTransaction: true),
            dependencies.Registers.Object,
            dependencies.Resources.Object);

        (await sut.GetNetByDimensionSetAsync(RegisterId, DimensionSetId, "amount")).Should().Be(12.5m);
        (await sut.GetNetByDimensionsAsync(
            RegisterId,
            [new DimensionValue(Guid.NewGuid(), Guid.NewGuid())],
            "amount")).Should().Be(12.5m);
    }

    [Fact]
    public async Task Admin_reader_returns_null_for_missing_registers_and_empty_children_for_present_registers()
    {
        var missingConnection = new RecordingDbConnection(_ => EmptyRegisterRows());
        var missing = new PostgresOperationalRegisterAdminReader(new RecordingUnitOfWork(missingConnection));

        (await missing.GetDetailsByIdAsync(RegisterId)).Should().BeNull();
        (await missing.GetDetailsByCodeAsync("  MiSsInG  ")).Should().BeNull();

        var presentConnection = new RecordingDbConnection(sql =>
        {
            if (sql.Contains("FROM operational_registers", StringComparison.Ordinal))
                return RegisterRows();
            if (sql.Contains("FROM operational_register_resources", StringComparison.Ordinal))
                return EmptyResourceRows();
            if (sql.Contains("FROM operational_register_dimension_rules", StringComparison.Ordinal))
                return EmptyRuleRows();
            throw new InvalidOperationException($"Unexpected SQL: {sql}");
        });
        var present = new PostgresOperationalRegisterAdminReader(new RecordingUnitOfWork(presentConnection));

        var byId = await present.GetDetailsByIdAsync(RegisterId);
        var byCode = await present.GetDetailsByCodeAsync("  SALES  ");

        byId.Should().NotBeNull();
        byId!.Resources.Should().BeEmpty();
        byId.DimensionRules.Should().BeEmpty();
        byCode.Should().BeEquivalentTo(byId);
        presentConnection.Commands.Where(x => x.CommandText.Contains("code_norm =", StringComparison.Ordinal))
            .Single().ParametersSnapshot.Single(x => x.ParameterName == "CodeNorm").Value.Should().Be("sales");

        var populatedConnection = new RecordingDbConnection(sql =>
        {
            if (sql.Contains("FROM operational_registers", StringComparison.Ordinal))
                return RegisterRows();
            if (sql.Contains("FROM operational_register_resources", StringComparison.Ordinal))
                return ResourceRows();
            if (sql.Contains("FROM operational_register_dimension_rules", StringComparison.Ordinal))
                return RuleRows();
            throw new InvalidOperationException($"Unexpected SQL: {sql}");
        });
        var populated = await new PostgresOperationalRegisterAdminReader(
            new RecordingUnitOfWork(populatedConnection)).GetDetailsByIdAsync(RegisterId);
        populated!.Resources.Should().ContainSingle();
        populated.DimensionRules.Should().ContainSingle();
    }

    [Fact]
    public async Task Admin_reader_rejects_empty_id_and_blank_code()
    {
        var sut = new PostgresOperationalRegisterAdminReader(new RecordingUnitOfWork(new RecordingDbConnection()));
        Func<Task> emptyId = () => sut.GetDetailsByIdAsync(Guid.Empty);
        Func<Task> blankCode = () => sut.GetDetailsByCodeAsync(" ");

        await emptyId.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await blankCode.Should().ThrowAsync<NgbArgumentRequiredException>();
    }

    [Fact]
    public async Task Movements_query_reader_returns_empty_when_physical_table_is_absent_for_both_queries()
    {
        var dependencies = RegisterDependencies([]);
        var dimensionSets = new Mock<IDimensionSetReader>(MockBehavior.Strict);
        var enrichment = new Mock<IDimensionValueEnrichmentReader>(MockBehavior.Strict);
        var sut = new PostgresOperationalRegisterMovementsQueryReader(
            new RecordingUnitOfWork(new RecordingDbConnection(scalar: _ => false)),
            dependencies.Registers.Object,
            dependencies.Resources.Object,
            dimensionSets.Object,
            enrichment.Object);
        var month = new DateOnly(2026, 8, 1);

        (await sut.GetMaxPeriodMonthAsync(RegisterId)).Should().BeNull();
        (await sut.GetByMonthsAsync(RegisterId, month, month)).Should().BeEmpty();
        dependencies.Registers.Verify(x => x.GetByIdAsync(RegisterId, It.IsAny<CancellationToken>()), Times.Exactly(2));
        dependencies.Resources.Verify(x => x.GetByRegisterIdAsync(RegisterId, It.IsAny<CancellationToken>()), Times.Exactly(2));
        dimensionSets.VerifyNoOtherCalls();
        enrichment.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Movements_query_reader_defaults_a_missing_resource_column_and_unresolved_dimension_bag()
    {
        var dependencies = RegisterDependencies([new("Amount", "amount", "amount", "Amount", 1)]);
        var dimensionSets = new Mock<IDimensionSetReader>(MockBehavior.Strict);
        dimensionSets.Setup(x => x.GetBagsByIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(DimensionSetId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DimensionBag>());
        var enrichment = new Mock<IDimensionValueEnrichmentReader>(MockBehavior.Strict);
        var documentId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
        var connection = new RecordingDbConnection(
            readerFactory: _ => MovementQueryRows(documentId, occurredAt),
            scalar: _ => true);
        var sut = new PostgresOperationalRegisterMovementsQueryReader(
            new RecordingUnitOfWork(connection),
            dependencies.Registers.Object,
            dependencies.Resources.Object,
            dimensionSets.Object,
            enrichment.Object);
        var month = new DateOnly(2026, 8, 1);

        var row = (await sut.GetByMonthsAsync(RegisterId, month, month)).Should().ContainSingle().Subject;

        row.MovementId.Should().Be(7);
        row.DocumentId.Should().Be(documentId);
        row.OccurredAtUtc.Should().Be(occurredAt);
        row.PeriodMonth.Should().Be(month);
        row.DimensionSetId.Should().Be(DimensionSetId);
        row.IsStorno.Should().BeTrue();
        row.Values.Should().Contain("amount", 0m);
        row.Dimensions.Should().BeSameAs(DimensionBag.Empty);
        row.DimensionValueDisplays.Should().BeEmpty();
        dimensionSets.VerifyAll();
        enrichment.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Movements_query_reader_maps_present_resources_cached_context_and_resolved_dimensions()
    {
        var dependencies = RegisterDependencies([new("Amount", "amount", "amount", "Amount", 1)]);
        var dimensionId = Guid.NewGuid();
        var valueId = Guid.NewGuid();
        var bag = new DimensionBag([new DimensionValue(dimensionId, valueId)]);
        var dimensionSets = new Mock<IDimensionSetReader>(MockBehavior.Strict);
        dimensionSets.Setup(x => x.GetBagsByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DimensionBag> { [DimensionSetId] = bag });
        var enrichment = new Mock<IDimensionValueEnrichmentReader>(MockBehavior.Strict);
        enrichment.Setup(x => x.ResolveAsync(
                It.IsAny<IReadOnlyCollection<DimensionValueKey>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<DimensionValueKey, string>
            {
                [new(dimensionId, valueId)] = "Resolved"
            });
        var documentId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
        var connection = new RecordingDbConnection(
            readerFactory: _ => MovementQueryRows(documentId, occurredAt, 12.5m),
            scalar: sql => sql.Contains("to_regclass", StringComparison.Ordinal)
                ? true
                : new DateOnly(2026, 8, 1));
        var sut = new PostgresOperationalRegisterMovementsQueryReader(
            new RecordingUnitOfWork(connection),
            dependencies.Registers.Object,
            dependencies.Resources.Object,
            dimensionSets.Object,
            enrichment.Object);
        var month = new DateOnly(2026, 8, 1);

        (await sut.GetMaxPeriodMonthAsync(RegisterId)).Should().Be(month);
        var row = (await sut.GetByMonthsAsync(RegisterId, month, month)).Should().ContainSingle().Subject;
        row.Values.Should().Contain("amount", 12.5m);
        row.Dimensions.Should().BeSameAs(bag);
        row.DimensionValueDisplays.Should().Contain(dimensionId, "Resolved");

        (await sut.GetByMonthsAsync(RegisterId, month, month)).Should().ContainSingle();
        dependencies.Registers.Verify(x => x.GetByIdAsync(RegisterId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Movements_query_reader_supports_a_present_table_without_resource_columns()
    {
        var dependencies = RegisterDependencies([]);
        var dimensionSets = new Mock<IDimensionSetReader>(MockBehavior.Strict);
        dimensionSets.Setup(x => x.GetBagsByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DimensionBag>());
        var enrichment = new Mock<IDimensionValueEnrichmentReader>(MockBehavior.Strict);
        var sut = new PostgresOperationalRegisterMovementsQueryReader(
            new RecordingUnitOfWork(new RecordingDbConnection(
                readerFactory: _ => MovementQueryRows(Guid.NewGuid(), DateTime.UnixEpoch),
                scalar: _ => true)),
            dependencies.Registers.Object,
            dependencies.Resources.Object,
            dimensionSets.Object,
            enrichment.Object);

        var rows = await sut.GetByMonthsAsync(RegisterId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1));
        rows.Should().ContainSingle().Which.Values.Should().BeEmpty();
    }

    [Fact]
    public async Task Movements_reader_returns_empty_for_absent_table_and_maps_database_null_resource_to_zero()
    {
        var dependencies = RegisterDependencies([new("Amount", "amount", "amount", "Amount", 1)]);
        var absent = new PostgresOperationalRegisterMovementsReader(
            new RecordingUnitOfWork(new RecordingDbConnection(scalar: _ => false)),
            dependencies.Registers.Object,
            dependencies.Resources.Object);
        var month = new DateOnly(2026, 8, 22);

        (await absent.GetByMonthAsync(RegisterId, month)).Should().BeEmpty();
        (await absent.GetDistinctMonthsByDocumentAsync(RegisterId, Guid.NewGuid())).Should().BeEmpty();

        var documentId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
        var present = new PostgresOperationalRegisterMovementsReader(
            new RecordingUnitOfWork(new RecordingDbConnection(
                readerFactory: _ => MovementRows(documentId, occurredAt),
                scalar: _ => true)),
            dependencies.Registers.Object,
            dependencies.Resources.Object);

        var row = (await present.GetByMonthAsync(RegisterId, month)).Should().ContainSingle().Subject;
        row.MovementId.Should().Be(9);
        row.DocumentId.Should().Be(documentId);
        row.OccurredAtUtc.Should().Be(occurredAt);
        row.DimensionSetId.Should().Be(DimensionSetId);
        row.IsStorno.Should().BeFalse();
        row.Resources.Should().Contain("amount", 0m);

        var missingColumnReader = new PostgresOperationalRegisterMovementsReader(
            new RecordingUnitOfWork(new RecordingDbConnection(
                readerFactory: _ => MovementRows(documentId, occurredAt, includeAmount: false),
                scalar: _ => true)),
            dependencies.Registers.Object,
            dependencies.Resources.Object);
        (await missingColumnReader.GetByMonthAsync(RegisterId, month)).Should()
            .ContainSingle().Which.Resources.Should().Contain("amount", 0m);

        var noResources = RegisterDependencies([]);
        var noResourceReader = new PostgresOperationalRegisterMovementsReader(
            new RecordingUnitOfWork(new RecordingDbConnection(
                readerFactory: _ => MovementRows(documentId, occurredAt, includeAmount: false),
                scalar: _ => true)),
            noResources.Registers.Object,
            noResources.Resources.Object);
        (await noResourceReader.GetByMonthAsync(RegisterId, month)).Should()
            .ContainSingle().Which.Resources.Should().BeEmpty();

        var decimalReader = new PostgresOperationalRegisterMovementsReader(
            new RecordingUnitOfWork(new RecordingDbConnection(
                readerFactory: _ => MovementRows(documentId, occurredAt, amount: 4.25m),
                scalar: _ => true)),
            dependencies.Registers.Object,
            dependencies.Resources.Object);
        (await decimalReader.GetByMonthAsync(RegisterId, month)).Should()
            .ContainSingle().Which.Resources.Should().Contain("amount", 4.25m);
    }

    [Fact]
    public async Task Monthly_projection_aggregator_covers_validation_empty_resources_and_all_value_shapes()
    {
        var invalid = new PostgresOperationalRegisterMonthlyProjectionAggregator(null!, null!, null!);
        Func<Task> emptyRegister = () => invalid.AggregateMonthAsync(Guid.Empty, new DateOnly(2026, 8, 1));
        await emptyRegister.Should().ThrowAsync<NgbArgumentRequiredException>();

        var emptyResources = RegisterDependencies([]);
        var emptyResourceAggregator = new PostgresOperationalRegisterMonthlyProjectionAggregator(
            new RecordingUnitOfWork(new RecordingDbConnection(
                readerFactory: _ => ProjectionRows(includeAmount: false),
                scalar: _ => true)),
            emptyResources.Registers.Object,
            emptyResources.Resources.Object);
        (await emptyResourceAggregator.AggregateMonthAsync(RegisterId, new DateOnly(2026, 8, 1)))
            .Should().ContainSingle().Which.Values.Should().BeEmpty();

        foreach (var value in new object?[] { null, DBNull.Value, 0m })
        {
            var dependencies = RegisterDependencies([new("Amount", "amount", "amount", "Amount", 1)]);
            var aggregator = new PostgresOperationalRegisterMonthlyProjectionAggregator(
                new RecordingUnitOfWork(new RecordingDbConnection(
                    readerFactory: _ => ProjectionRows(value),
                    scalar: _ => true)),
                dependencies.Registers.Object,
                dependencies.Resources.Object);
            (await aggregator.AggregateMonthAsync(RegisterId, new DateOnly(2026, 8, 1))).Should().BeEmpty();
        }

        var missingDependencies = RegisterDependencies([new("Amount", "amount", "amount", "Amount", 1)]);
        var missingColumn = new PostgresOperationalRegisterMonthlyProjectionAggregator(
            new RecordingUnitOfWork(new RecordingDbConnection(
                readerFactory: _ => ProjectionRows(includeAmount: false),
                scalar: _ => true)),
            missingDependencies.Registers.Object,
            missingDependencies.Resources.Object);
        (await missingColumn.AggregateMonthAsync(RegisterId, new DateOnly(2026, 8, 1))).Should().BeEmpty();

        var nonZeroDependencies = RegisterDependencies([new("Amount", "amount", "amount", "Amount", 1)]);
        var nonZero = new PostgresOperationalRegisterMonthlyProjectionAggregator(
            new RecordingUnitOfWork(new RecordingDbConnection(
                readerFactory: _ => ProjectionRows(7.5m),
                scalar: _ => true)),
            nonZeroDependencies.Registers.Object,
            nonZeroDependencies.Resources.Object);
        (await nonZero.AggregateMonthAsync(RegisterId, new DateOnly(2026, 8, 1))).Should()
            .ContainSingle().Which.Values.Should().Contain("amount", 7.5m);
    }

    private static OperationalRegisterAdminItem Register()
        => new(RegisterId, "Sales", "sales", "sales", "Sales", false, DateTime.UnixEpoch, DateTime.UnixEpoch);

    private static DataTableReader RegisterRows()
    {
        var table = RegisterTable();
        table.Rows.Add(RegisterId, "Sales", "sales", "sales", "Sales", false, DateTime.UnixEpoch, DateTime.UnixEpoch);
        return table.CreateDataReader();
    }

    private static DataTableReader EmptyRegisterRows() => RegisterTable().CreateDataReader();

    private static DataTable RegisterTable()
    {
        var table = new DataTable();
        table.Columns.Add("RegisterId", typeof(Guid));
        table.Columns.Add("Code", typeof(string));
        table.Columns.Add("CodeNorm", typeof(string));
        table.Columns.Add("TableCode", typeof(string));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("HasMovements", typeof(bool));
        table.Columns.Add("CreatedAtUtc", typeof(DateTime));
        table.Columns.Add("UpdatedAtUtc", typeof(DateTime));
        return table;
    }

    private static DataTableReader EmptyResourceRows()
    {
        var table = new DataTable();
        table.Columns.Add("Code", typeof(string));
        table.Columns.Add("CodeNorm", typeof(string));
        table.Columns.Add("ColumnCode", typeof(string));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Ordinal", typeof(int));
        return table.CreateDataReader();
    }

    private static DataTableReader ResourceRows()
    {
        var table = new DataTable();
        table.Columns.Add("Code", typeof(string));
        table.Columns.Add("CodeNorm", typeof(string));
        table.Columns.Add("ColumnCode", typeof(string));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Ordinal", typeof(int));
        table.Rows.Add("Amount", "amount", "amount", "Amount", 1);
        return table.CreateDataReader();
    }

    private static DataTableReader EmptyRuleRows()
    {
        var table = new DataTable();
        table.Columns.Add("DimensionId", typeof(Guid));
        table.Columns.Add("DimensionCode", typeof(string));
        table.Columns.Add("Ordinal", typeof(int));
        table.Columns.Add("IsRequired", typeof(bool));
        return table.CreateDataReader();
    }

    private static DataTableReader RuleRows()
    {
        var table = new DataTable();
        table.Columns.Add("DimensionId", typeof(Guid));
        table.Columns.Add("DimensionCode", typeof(string));
        table.Columns.Add("Ordinal", typeof(int));
        table.Columns.Add("IsRequired", typeof(bool));
        table.Rows.Add(Guid.NewGuid(), "department", 1, true);
        return table.CreateDataReader();
    }

    private static (Mock<IOperationalRegisterRepository> Registers, Mock<IOperationalRegisterResourceRepository> Resources)
        RegisterDependencies(IReadOnlyList<OperationalRegisterResource> resourceRows)
    {
        var registers = new Mock<IOperationalRegisterRepository>(MockBehavior.Strict);
        registers.Setup(x => x.GetByIdAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register());
        var resources = new Mock<IOperationalRegisterResourceRepository>(MockBehavior.Strict);
        resources.Setup(x => x.GetByRegisterIdAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resourceRows);
        return (registers, resources);
    }

    private static DataTableReader MovementQueryRows(Guid documentId, DateTime occurredAt, object? amount = null)
    {
        var table = new DataTable();
        table.Columns.Add("MovementId", typeof(long));
        table.Columns.Add("DocumentId", typeof(Guid));
        table.Columns.Add("OccurredAtUtc", typeof(DateTime));
        table.Columns.Add("PeriodMonth", typeof(DateOnly));
        table.Columns.Add("DimensionSetId", typeof(Guid));
        table.Columns.Add("IsStorno", typeof(bool));
        if (amount is not null)
            table.Columns.Add("amount", typeof(decimal));
        if (amount is null)
            table.Rows.Add(7L, documentId, occurredAt, new DateOnly(2026, 8, 1), DimensionSetId, true);
        else
            table.Rows.Add(7L, documentId, occurredAt, new DateOnly(2026, 8, 1), DimensionSetId, true, amount);
        return table.CreateDataReader();
    }

    private static DataTableReader MovementRows(
        Guid documentId,
        DateTime occurredAt,
        object? amount = null,
        bool includeAmount = true)
    {
        var table = new DataTable();
        table.Columns.Add("MovementId", typeof(long));
        table.Columns.Add("DocumentId", typeof(Guid));
        table.Columns.Add("OccurredAtUtc", typeof(DateTime));
        table.Columns.Add("DimensionSetId", typeof(Guid));
        table.Columns.Add("IsStorno", typeof(bool));
        if (includeAmount)
            table.Columns.Add("amount", typeof(decimal));
        if (includeAmount)
            table.Rows.Add(9L, documentId, occurredAt, DimensionSetId, false, amount ?? DBNull.Value);
        else
            table.Rows.Add(9L, documentId, occurredAt, DimensionSetId, false);
        return table.CreateDataReader();
    }

    private static DataTableReader ProjectionRows(object? amount = null, bool includeAmount = true)
    {
        var table = new DataTable();
        table.Columns.Add("DimensionSetId", typeof(Guid));
        if (includeAmount)
            table.Columns.Add("amount", typeof(decimal));
        if (includeAmount)
            table.Rows.Add(DimensionSetId, amount ?? DBNull.Value);
        else
            table.Rows.Add(DimensionSetId);
        return table.CreateDataReader();
    }
}
