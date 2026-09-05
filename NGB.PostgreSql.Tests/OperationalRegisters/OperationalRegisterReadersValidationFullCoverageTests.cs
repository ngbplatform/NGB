using System.Data;
using FluentAssertions;
using Moq;
using NGB.Core.Dimensions;
using NGB.Core.Dimensions.Enrichment;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.OperationalRegisters;
using NGB.PostgreSql.OperationalRegisters;
using NGB.PostgreSql.Schema;
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
    public async Task Resource_net_reader_validates_all_query_shapes_before_database_access()
    {
        var sut = new PostgresOperationalRegisterResourceNetReader(null!, null!, null!);

        Func<Task> emptyRegister = () => sut.GetNetByDimensionSetAsync(Guid.Empty, DimensionSetId, "amount");
        Func<Task> emptySet = () => sut.GetNetByDimensionSetAsync(RegisterId, Guid.Empty, "amount");
        Func<Task> blankSetResource = () => sut.GetNetByDimensionSetAsync(RegisterId, DimensionSetId, " ");
        Func<Task> emptySetBatchRegister = () => sut.GetNetByDimensionSetsAsync(
            Guid.Empty, [DimensionSetId], "amount");
        Func<Task> nullSetBatch = () => sut.GetNetByDimensionSetsAsync(
            RegisterId, null!, "amount");
        Func<Task> setBatchWithEmptyId = () => sut.GetNetByDimensionSetsAsync(
            RegisterId, [DimensionSetId, Guid.Empty], "amount");
        Func<Task> blankSetBatchResource = () => sut.GetNetByDimensionSetsAsync(
            RegisterId, [DimensionSetId], " ");
        Func<Task> emptyDimensionsRegister = () => sut.GetNetByDimensionsAsync(Guid.Empty, [], "amount");
        Func<Task> nullDimensions = () => sut.GetNetByDimensionsAsync(RegisterId, null!, "amount");
        Func<Task> emptyDimensions = () => sut.GetNetByDimensionsAsync(RegisterId, [], "amount");
        Func<Task> blankDimensionsResource = () => sut.GetNetByDimensionsAsync(
            RegisterId, [new DimensionValue(Guid.NewGuid(), Guid.NewGuid())], "\t");
        Func<Task> emptyBatchRegister = () => sut.GetNetsByDimensionsAsync(
            Guid.Empty, [], "amount", DateOnly.MaxValue);
        Func<Task> nullBatch = () => sut.GetNetsByDimensionsAsync(
            RegisterId, null!, "amount", DateOnly.MaxValue);
        Func<Task> emptyBatchGroup = () => sut.GetNetsByDimensionsAsync(
            RegisterId, [[]], "amount", DateOnly.MaxValue);
        Func<Task> nullBatchGroup = () => sut.GetNetsByDimensionsAsync(
            RegisterId, [null!], "amount", DateOnly.MaxValue);
        Func<Task> blankBatchResource = () => sut.GetNetsByDimensionsAsync(
            RegisterId,
            [[new DimensionValue(Guid.NewGuid(), Guid.NewGuid())]],
            " ",
            DateOnly.MaxValue);

        await emptyRegister.Should().ThrowAsync<NgbArgumentInvalidException>();
        await emptySet.Should().ThrowAsync<NgbArgumentInvalidException>();
        await blankSetResource.Should().ThrowAsync<NgbArgumentRequiredException>();
        await emptySetBatchRegister.Should().ThrowAsync<NgbArgumentInvalidException>();
        await nullSetBatch.Should().ThrowAsync<ArgumentNullException>();
        await setBatchWithEmptyId.Should().ThrowAsync<NgbArgumentInvalidException>();
        await blankSetBatchResource.Should().ThrowAsync<NgbArgumentRequiredException>();
        (await sut.GetNetByDimensionSetsAsync(RegisterId, [], "amount")).Should().BeEmpty();
        await emptyDimensionsRegister.Should().ThrowAsync<NgbArgumentInvalidException>();
        await nullDimensions.Should().ThrowAsync<ArgumentNullException>();
        await emptyDimensions.Should().ThrowAsync<NgbArgumentInvalidException>();
        await blankDimensionsResource.Should().ThrowAsync<NgbArgumentRequiredException>();
        await emptyBatchRegister.Should().ThrowAsync<NgbArgumentInvalidException>();
        await nullBatch.Should().ThrowAsync<ArgumentNullException>();
        await emptyBatchGroup.Should().ThrowAsync<NgbArgumentInvalidException>();
        await nullBatchGroup.Should().ThrowAsync<NgbArgumentInvalidException>();
        await blankBatchResource.Should().ThrowAsync<NgbArgumentRequiredException>();
        (await sut.GetNetsByDimensionsAsync(RegisterId, [], "amount", DateOnly.MaxValue)).Should().BeEmpty();
    }

    [Fact]
    public async Task Resource_net_reader_rejects_a_resource_not_declared_by_the_register_for_all_query_shapes()
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
        Func<Task> bySetBatch = () => sut.GetNetByDimensionSetsAsync(
            RegisterId, [DimensionSetId], "amount");
        Func<Task> byDimensions = () => sut.GetNetByDimensionsAsync(
            RegisterId, [new DimensionValue(Guid.NewGuid(), Guid.NewGuid())], "amount");
        Func<Task> byDimensionBatch = () => sut.GetNetsByDimensionsAsync(
            RegisterId,
            [[new DimensionValue(Guid.NewGuid(), Guid.NewGuid())]],
            "amount",
            DateOnly.MaxValue);

        await bySet.Should().ThrowAsync<NgbConfigurationViolationException>();
        await bySetBatch.Should().ThrowAsync<NgbConfigurationViolationException>();
        await byDimensions.Should().ThrowAsync<NgbConfigurationViolationException>();
        await byDimensionBatch.Should().ThrowAsync<NgbConfigurationViolationException>();
        registers.VerifyAll();
        resources.VerifyAll();
    }

    [Fact]
    public async Task Resource_net_reader_returns_zero_when_the_physical_table_does_not_exist_for_all_query_shapes()
    {
        var dependencies = RegisterDependencies([new("Amount", "amount", "amount", "Amount", 1)]);
        var sut = new PostgresOperationalRegisterResourceNetReader(
            new RecordingUnitOfWork(
                new RecordingDbConnection(scalar: _ => false),
                hasActiveTransaction: true),
            dependencies.Registers.Object,
            dependencies.Resources.Object);

        (await sut.GetNetByDimensionSetAsync(RegisterId, DimensionSetId, "amount")).Should().Be(0m);
        (await sut.GetNetByDimensionSetsAsync(RegisterId, [DimensionSetId], "amount"))
            .Should().Contain(DimensionSetId, 0m);
        (await sut.GetNetByDimensionsAsync(
            RegisterId,
            [new DimensionValue(Guid.NewGuid(), Guid.NewGuid())],
            "amount")).Should().Be(0m);
        (await sut.GetNetsByDimensionsAsync(
            RegisterId,
            [
                [new DimensionValue(Guid.NewGuid(), Guid.NewGuid())],
                [new DimensionValue(Guid.NewGuid(), Guid.NewGuid())]
            ],
            "amount",
            DateOnly.MaxValue)).Should().Equal(0m, 0m);

        dependencies.Registers.Verify(x => x.GetByIdAsync(RegisterId, It.IsAny<CancellationToken>()), Times.Once);
        dependencies.Resources.Verify(x => x.GetByRegisterIdAsync(RegisterId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Resource_net_reader_returns_database_net_when_the_physical_table_exists()
    {
        var dependencies = RegisterDependencies([new("Amount", "amount", "amount", "Amount", 1)]);
        var connection = new RecordingDbConnection(
            readerFactory: _ => ResourceNetBySetRows((DimensionSetId, 12.5m)),
            scalar: sql => sql.Contains("to_regclass", StringComparison.Ordinal) ? true : 12.5m);
        var sut = new PostgresOperationalRegisterResourceNetReader(
            new RecordingUnitOfWork(connection, hasActiveTransaction: true),
            dependencies.Registers.Object,
            dependencies.Resources.Object);

        (await sut.GetNetByDimensionSetAsync(RegisterId, DimensionSetId, "amount")).Should().Be(12.5m);
        (await sut.GetNetByDimensionsAsync(
            RegisterId,
            [new DimensionValue(Guid.NewGuid(), Guid.NewGuid())],
            "amount")).Should().Be(12.5m);
        (await sut.GetNetByDimensionSetsAsync(
                RegisterId, [DimensionSetId, DimensionSetId], "amount"))
            .Should().Contain(DimensionSetId, 12.5m).And.HaveCount(1);
    }

    [Fact]
    public async Task Resource_net_reader_batches_point_in_time_dimension_groups_in_one_query()
    {
        var dependencies = RegisterDependencies([new("Quantity", "quantity", "quantity", "Quantity", 1)]);
        var firstDimension = new DimensionValue(Guid.NewGuid(), Guid.NewGuid());
        var secondDimension = new DimensionValue(Guid.NewGuid(), Guid.NewGuid());
        var connection = new RecordingDbConnection(
            readerFactory: sql => sql.Contains("request_numbers", StringComparison.Ordinal)
                ? ResourceNetRequestRows()
                : new DataTable().CreateDataReader(),
            scalar: sql => sql.Contains("to_regclass", StringComparison.Ordinal) ? true : null);
        var sut = new PostgresOperationalRegisterResourceNetReader(
            new RecordingUnitOfWork(connection, hasActiveTransaction: true),
            dependencies.Registers.Object,
            dependencies.Resources.Object);

        var result = await sut.GetNetsByDimensionsAsync(
            RegisterId,
            [
                [firstDimension, firstDimension],
                [firstDimension, secondDimension]
            ],
            "quantity",
            DateOnly.MaxValue);

        result.Should().Equal(5m, -2m);
        var query = connection.Commands.Single(command => command.CommandText.Contains("request_numbers", StringComparison.Ordinal));
        query.CommandText.Should().Contain("movement.period_month <= @AsOfMonth")
            .And.Contain("movement.occurred_at_utc < @OccurredToExclusiveUtc");
        query.ParametersSnapshot.Should().Contain(parameter =>
            parameter.ParameterName.TrimStart('@').StartsWith("RequestIndexes", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Resource_net_reader_batch_uses_movement_only_query_when_balances_table_is_absent()
    {
        var dependencies = RegisterDependencies([new("Quantity", "quantity", "quantity", "Quantity", 1)]);
        var probeCount = 0;
        var connection = new RecordingDbConnection(
            readerFactory: sql => sql.Contains("request_numbers", StringComparison.Ordinal)
                ? ResourceNetRequestRows()
                : new DataTable().CreateDataReader(),
            scalar: _ => Interlocked.Increment(ref probeCount) == 1);
        var sut = new PostgresOperationalRegisterResourceNetReader(
            new RecordingUnitOfWork(connection, hasActiveTransaction: true),
            dependencies.Registers.Object,
            dependencies.Resources.Object);

        var result = await sut.GetNetsByDimensionsAsync(
            RegisterId,
            [
                [new DimensionValue(Guid.NewGuid(), Guid.NewGuid())],
                [new DimensionValue(Guid.NewGuid(), Guid.NewGuid())]
            ],
            "quantity",
            new DateOnly(2026, 8, 1));

        result.Should().Equal(5m, -2m);
        probeCount.Should().Be(2);
        connection.Commands.Last().CommandText.Should()
            .Contain("LEFT JOIN opreg_sales__movements movement")
            .And.NotContain("latest_snapshot");
    }

    [Fact]
    public async Task Resource_net_reader_point_queries_use_movement_only_sql_without_balance_snapshots()
    {
        static PostgresOperationalRegisterResourceNetReader Create(out RecordingDbConnection connection)
        {
            var dependencies = RegisterDependencies([new("Amount", "amount", "amount", "Amount", 1)]);
            var probeCount = 0;
            connection = new RecordingDbConnection(
                readerFactory: _ => ResourceNetBySetRows((DimensionSetId, 2m)),
                scalar: sql => sql.Contains("to_regclass", StringComparison.Ordinal)
                    ? Interlocked.Increment(ref probeCount) == 1
                    : 2m);
            return new PostgresOperationalRegisterResourceNetReader(
                new RecordingUnitOfWork(connection, hasActiveTransaction: true),
                dependencies.Registers.Object,
                dependencies.Resources.Object);
        }

        var bySet = Create(out var bySetConnection);
        (await bySet.GetNetByDimensionSetAsync(RegisterId, DimensionSetId, "amount")).Should().Be(2m);
        bySetConnection.Commands.Last().CommandText.Should().NotContain("latest_snapshot");

        var bySets = Create(out var bySetsConnection);
        (await bySets.GetNetByDimensionSetsAsync(RegisterId, [DimensionSetId], "amount"))
            .Should().Contain(DimensionSetId, 2m);
        bySetsConnection.Commands.Last().CommandText.Should().NotContain("latest_snapshot");

        var byDimensions = Create(out var byDimensionsConnection);
        (await byDimensions.GetNetByDimensionsAsync(
            RegisterId,
            [new DimensionValue(Guid.NewGuid(), Guid.NewGuid())],
            "amount")).Should().Be(2m);
        byDimensionsConnection.Commands.Last().CommandText.Should().NotContain("latest_snapshot");

        Action invalidSuffix = () => PostgresOperationalRegisterResourceNetReader
            .ResolveBalancesTableName("opreg_sales");
        invalidSuffix.Should().Throw<NgbConfigurationViolationException>();
        PostgresOperationalRegisterResourceNetReader.ResolveBalancesTableName("opreg_sales__movements")
            .Should().Be("opreg_sales__balances");
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
        (await sut.GetResourceNetsByDimensionAsync(
            RegisterId, month, month, null, Guid.NewGuid(), "amount")).Should().BeEmpty();
        dependencies.Registers.Verify(x => x.GetByIdAsync(RegisterId, It.IsAny<CancellationToken>()), Times.Exactly(3));
        dependencies.Resources.Verify(x => x.GetByRegisterIdAsync(RegisterId, It.IsAny<CancellationToken>()), Times.Exactly(3));
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
    public async Task Occurred_at_cursor_query_covers_seek_dimensions_resources_and_max_date_boundary()
    {
        var dependencies = RegisterDependencies(
        [
            new("Quantity", "quantity", "quantity", "Quantity", 2),
            new("Amount", "amount", "amount", "Amount", 1)
        ]);
        var dimensionSets = new Mock<IDimensionSetReader>(MockBehavior.Strict);
        dimensionSets.Setup(x => x.GetBagsByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DimensionBag>());
        var enrichment = new Mock<IDimensionValueEnrichmentReader>(MockBehavior.Strict);
        var documentId = Guid.CreateVersion7();
        var occurredAt = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
        var connection = new RecordingDbConnection(
            readerFactory: _ => MovementQueryRows(documentId, occurredAt, 12.5m),
            scalar: _ => true);
        var sut = new PostgresOperationalRegisterMovementsQueryReader(
            new RecordingUnitOfWork(connection),
            dependencies.Registers.Object,
            dependencies.Resources.Object,
            dimensionSets.Object,
            enrichment.Object,
            new OperationalRegisterMetadataCache(TimeProvider.System),
            new PostgresRelationPresenceCache(TimeProvider.System));
        var filter = new DimensionValue(Guid.CreateVersion7(), Guid.CreateVersion7());

        var rows = await sut.GetByOccurredAtCursorAsync(
            RegisterId,
            new DateOnly(2026, 8, 1),
            DateOnly.MaxValue,
            [filter],
            new OperationalRegisterOccurredAtCursor(occurredAt.AddTicks(-1), 6),
            limit: 2);

        rows.Should().ContainSingle().Which.Values.Should().Contain("amount", 12.5m);
        var command = connection.Commands.Last();
        command.CommandText.Should().Contain("matching_dimension_sets")
            .And.Contain("@AfterOccurredAtUtc")
            .And.Contain("amount AS \"amount\"");
        command.ParametersSnapshot.Should().Contain(parameter =>
            parameter.ParameterName == "OccurredToExclusiveUtc"
            && Equals(parameter.Value, DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc)));

        var noResourceDependencies = RegisterDependencies([]);
        var noResourceReader = new PostgresOperationalRegisterMovementsQueryReader(
            new RecordingUnitOfWork(new RecordingDbConnection(
                readerFactory: _ => MovementQueryRows(documentId, occurredAt),
                scalar: _ => true)),
            noResourceDependencies.Registers.Object,
            noResourceDependencies.Resources.Object,
            dimensionSets.Object,
            enrichment.Object);
        (await noResourceReader.GetByOccurredAtCursorAsync(
                RegisterId,
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 31),
                cursor: null,
                limit: 2))
            .Should().ContainSingle().Which.Values.Should().BeEmpty();

        var dbNullReader = new PostgresOperationalRegisterMovementsQueryReader(
            new RecordingUnitOfWork(new RecordingDbConnection(
                readerFactory: _ => MovementQueryRows(documentId, occurredAt, DBNull.Value),
                scalar: _ => true)),
            dependencies.Registers.Object,
            dependencies.Resources.Object,
            dimensionSets.Object,
            enrichment.Object);
        (await dbNullReader.GetByOccurredAtCursorAsync(
                RegisterId,
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 31),
                limit: 2))
            .Should().ContainSingle().Which.Values.Should().Contain("amount", 0m);
    }

    [Fact]
    public async Task Occurred_at_offset_page_maps_total_and_recovers_total_beyond_last_row()
    {
        var dependencies = RegisterDependencies([new("Amount", "amount", "amount", "Amount", 1)]);
        var dimensionSets = new Mock<IDimensionSetReader>(MockBehavior.Strict);
        dimensionSets.Setup(x => x.GetBagsByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DimensionBag>());
        var enrichment = new Mock<IDimensionValueEnrichmentReader>(MockBehavior.Strict);
        var documentId = Guid.CreateVersion7();
        var occurredAt = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
        var populatedConnection = new RecordingDbConnection(
            readerFactory: _ => MovementPageRows(documentId, occurredAt, total: 3),
            scalar: _ => true);
        var populated = new PostgresOperationalRegisterMovementsQueryReader(
            new RecordingUnitOfWork(populatedConnection),
            dependencies.Registers.Object,
            dependencies.Resources.Object,
            dimensionSets.Object,
            enrichment.Object);

        var page = await populated.GetByOccurredAtPageAsync(
            RegisterId,
            new DateOnly(2026, 8, 1),
            DateOnly.MaxValue,
            offset: 0,
            limit: 2);

        page.Rows.Should().ContainSingle();
        page.Total.Should().Be(3);

        var emptyDependencies = RegisterDependencies([]);
        var emptyConnection = new RecordingDbConnection(
            readerFactory: _ => EmptyMovementPageRows(),
            scalar: sql => sql.Contains("to_regclass", StringComparison.Ordinal) ? true : 3L);
        var beyondLast = new PostgresOperationalRegisterMovementsQueryReader(
            new RecordingUnitOfWork(emptyConnection),
            emptyDependencies.Registers.Object,
            emptyDependencies.Resources.Object,
            Mock.Of<IDimensionSetReader>(MockBehavior.Strict),
            Mock.Of<IDimensionValueEnrichmentReader>(MockBehavior.Strict));

        var emptyPage = await beyondLast.GetByOccurredAtPageAsync(
            RegisterId,
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31),
            offset: 999,
            limit: 2);

        emptyPage.Rows.Should().BeEmpty();
        emptyPage.Total.Should().Be(3);
        emptyConnection.Commands.Should().Contain(command =>
            command.CommandText.Contains("SELECT COUNT(*)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Resource_net_page_covers_dimension_filter_totals_display_and_legacy_wrapper()
    {
        var dependencies = RegisterDependencies([new("Amount", "amount", "amount", "Amount", 1)]);
        var groupDimensionId = Guid.CreateVersion7();
        var valueId = Guid.CreateVersion7();
        var enrichment = new Mock<IDimensionValueEnrichmentReader>(MockBehavior.Strict);
        enrichment.Setup(x => x.ResolveAsync(
                It.Is<IReadOnlyCollection<DimensionValueKey>>(keys =>
                    keys.Count == 1 && keys.Single() == new DimensionValueKey(groupDimensionId, valueId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<DimensionValueKey, string>
            {
                [new(groupDimensionId, valueId)] = "Customer"
            });
        var connection = new RecordingDbConnection(
            readerFactory: _ => GroupNetRows(valueId, -4m, 7, 12m, 4m),
            scalar: _ => true);
        var sut = new PostgresOperationalRegisterMovementsQueryReader(
            new RecordingUnitOfWork(connection),
            dependencies.Registers.Object,
            dependencies.Resources.Object,
            Mock.Of<IDimensionSetReader>(MockBehavior.Strict),
            enrichment.Object);
        var filter = new DimensionValue(Guid.CreateVersion7(), Guid.CreateVersion7());

        var page = await sut.GetResourceNetsByDimensionPageAsync(
            RegisterId,
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 1),
            [filter],
            groupDimensionId,
            "amount",
            offset: 2,
            limit: 3);

        page.Rows.Should().ContainSingle().Which.Should().Be(
            new OperationalRegisterDimensionResourceNetRow(valueId, -4m, "Customer"));
        page.Total.Should().Be(7);
        page.TotalPositive.Should().Be(12m);
        page.TotalNegativeAbsolute.Should().Be(4m);
        connection.Commands.Last().CommandText.Should().Contain("matching_dimension_sets")
            .And.Contain("OFFSET @Offset");

        (await sut.GetResourceNetsByDimensionAsync(
                RegisterId,
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 1),
                null,
                groupDimensionId,
                "amount"))
            .Should().ContainSingle();

        await ((Func<Task>)(() => sut.GetResourceNetsByDimensionPageAsync(
                RegisterId,
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 1),
                null,
                groupDimensionId,
                "quantity",
                0,
                10)))
            .Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    [Fact]
    public async Task Resource_net_page_returns_zero_totals_without_display_lookup_for_empty_query()
    {
        var dependencies = RegisterDependencies([new("Amount", "amount", "amount", "Amount", 1)]);
        var enrichment = new Mock<IDimensionValueEnrichmentReader>(MockBehavior.Strict);
        var sut = new PostgresOperationalRegisterMovementsQueryReader(
            new RecordingUnitOfWork(new RecordingDbConnection(
                readerFactory: _ => GroupNetRowsEmpty(),
                scalar: _ => true)),
            dependencies.Registers.Object,
            dependencies.Resources.Object,
            Mock.Of<IDimensionSetReader>(MockBehavior.Strict),
            enrichment.Object);

        var page = await sut.GetResourceNetsByDimensionPageAsync(
            RegisterId,
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 1),
            null,
            Guid.CreateVersion7(),
            "amount",
            0,
            10);

        page.Rows.Should().BeEmpty();
        page.Total.Should().Be(0);
        page.TotalPositive.Should().Be(0m);
        page.TotalNegativeAbsolute.Should().Be(0m);
        enrichment.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Legacy_unpaged_net_and_balance_queries_reject_results_above_materialization_budget()
    {
        var dependencies = RegisterDependencies([new("Amount", "amount", "amount", "Amount", 1)]);
        var valueId = Guid.CreateVersion7();
        var total = NGB.Contracts.Common.PagingLimits.MaxMaterializedRows + 1;
        var connection = new RecordingDbConnection(
            readerFactory: _ => GroupNetRows(valueId, 1m, total, 1m, 0m),
            scalar: _ => true);
        var enrichment = new Mock<IDimensionValueEnrichmentReader>(MockBehavior.Strict);
        enrichment.Setup(x => x.ResolveAsync(
                It.IsAny<IReadOnlyCollection<DimensionValueKey>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<DimensionValueKey, string>());
        var sut = new PostgresOperationalRegisterMovementsQueryReader(
            new RecordingUnitOfWork(connection),
            dependencies.Registers.Object,
            dependencies.Resources.Object,
            Mock.Of<IDimensionSetReader>(MockBehavior.Strict),
            enrichment.Object);
        var dimensionId = Guid.CreateVersion7();

        await ((Func<Task>)(() => sut.GetResourceNetsByDimensionAsync(
                RegisterId,
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 1),
                null,
                dimensionId,
                "amount")))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>()
            .WithMessage("*Use the paged API*");

        await ((Func<Task>)(() => sut.GetResourceBalancesByDimensionAsync(
                RegisterId,
                new DateOnly(2026, 8, 1),
                null,
                dimensionId,
                "amount")))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>()
            .WithMessage("*Use the paged API*");
    }

    [Fact]
    public async Task Balance_page_falls_back_to_movement_aggregation_when_snapshot_table_is_absent()
    {
        var dependencies = RegisterDependencies([new("Amount", "amount", "amount", "Amount", 1)]);
        var valueId = Guid.CreateVersion7();
        var groupDimensionId = Guid.CreateVersion7();
        var probeCount = 0;
        var enrichment = new Mock<IDimensionValueEnrichmentReader>(MockBehavior.Strict);
        enrichment.Setup(x => x.ResolveAsync(
                It.IsAny<IReadOnlyCollection<DimensionValueKey>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<DimensionValueKey, string>());
        var connection = new RecordingDbConnection(
            readerFactory: _ => GroupNetRows(valueId, 2m, 1, 2m, 0m),
            scalar: _ => Interlocked.Increment(ref probeCount) == 1);
        var sut = new PostgresOperationalRegisterMovementsQueryReader(
            new RecordingUnitOfWork(connection),
            dependencies.Registers.Object,
            dependencies.Resources.Object,
            Mock.Of<IDimensionSetReader>(MockBehavior.Strict),
            enrichment.Object);

        var page = await sut.GetResourceBalancesByDimensionPageAsync(
            RegisterId,
            new DateOnly(2026, 8, 1),
            [new DimensionValue(Guid.CreateVersion7(), Guid.CreateVersion7())],
            groupDimensionId,
            "amount",
            0,
            10);

        page.Rows.Should().ContainSingle().Which.NetAmount.Should().Be(2m);
        var sql = connection.Commands.Last().CommandText;
        sql.Should().Contain("FROM opreg_sales__movements movement")
            .And.NotContain("latest_snapshot")
            .And.Contain("matching_dimension_sets");
        probeCount.Should().Be(2);
    }

    [Fact]
    public async Task Balance_cursor_page_uses_seek_and_carried_totals_without_repeating_windows()
    {
        var dependencies = RegisterDependencies([new("Amount", "amount", "amount", "Amount", 1)]);
        var valueId = Guid.NewGuid();
        var groupDimensionId = Guid.NewGuid();
        var dimensionSets = new Mock<IDimensionSetReader>(MockBehavior.Strict);
        var enrichment = new Mock<IDimensionValueEnrichmentReader>(MockBehavior.Strict);
        enrichment.Setup(x => x.ResolveAsync(
                It.IsAny<IReadOnlyCollection<DimensionValueKey>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<DimensionValueKey, string>
            {
                [new(groupDimensionId, valueId)] = "Resolved"
            });
        var connection = new RecordingDbConnection(
            readerFactory: _ => GroupNetRows(valueId, -4m, 7, 12m, 4m),
            scalar: _ => true);
        var sut = new PostgresOperationalRegisterMovementsQueryReader(
            new RecordingUnitOfWork(connection),
            dependencies.Registers.Object,
            dependencies.Resources.Object,
            dimensionSets.Object,
            enrichment.Object);

        var page = await sut.GetResourceBalancesByDimensionCursorAsync(
            RegisterId,
            new DateOnly(2026, 8, 1),
            dimensions: null,
            groupDimensionId,
            "amount",
            new OperationalRegisterDimensionResourceNetCursor(
                AfterPositiveGroup: true,
                AfterValueId: Guid.NewGuid(),
                NextOffset: 3,
                Total: 7,
                TotalPositive: 12m,
                TotalNegativeAbsolute: 4m),
            limit: 2);

        page.Rows.Should().ContainSingle().Which.Display.Should().Be("Resolved");
        page.Total.Should().Be(7);
        page.TotalPositive.Should().Be(12m);
        page.TotalNegativeAbsolute.Should().Be(4m);
        var sql = connection.Commands.Last().CommandText;
        sql.Should().Contain("@KnownTotal::integer")
            .And.Contain("@AfterGroupRank::integer")
            .And.NotContain(" OVER()")
            .And.NotContain("OFFSET");
        dimensionSets.VerifyNoOtherCalls();
        enrichment.VerifyAll();

        var legacyRows = await sut.GetResourceBalancesByDimensionAsync(
            RegisterId,
            new DateOnly(2026, 8, 1),
            dimensions: null,
            groupDimensionId,
            "amount");
        legacyRows.Should().ContainSingle().Which.Display.Should().Be("Resolved");
    }

    [Fact]
    public async Task Balance_cursor_page_preserves_empty_page_totals_and_covers_snapshot_source_boundaries()
    {
        var dependencies = RegisterDependencies([new("Amount", "amount", "amount", "Amount", 1)]);
        var groupDimensionId = Guid.CreateVersion7();
        var enrichment = new Mock<IDimensionValueEnrichmentReader>(MockBehavior.Strict);
        var snapshotReader = new PostgresOperationalRegisterMovementsQueryReader(
            new RecordingUnitOfWork(new RecordingDbConnection(
                readerFactory: _ => GroupNetRowsEmpty(),
                scalar: _ => true)),
            dependencies.Registers.Object,
            dependencies.Resources.Object,
            Mock.Of<IDimensionSetReader>(MockBehavior.Strict),
            enrichment.Object);

        var first = await snapshotReader.GetResourceBalancesByDimensionCursorAsync(
            RegisterId,
            new DateOnly(2026, 8, 1),
            dimensions: null,
            groupDimensionId,
            "amount",
            cursor: null,
            limit: 10);
        first.Rows.Should().BeEmpty();
        first.Total.Should().Be(0);
        first.TotalPositive.Should().Be(0m);
        first.TotalNegativeAbsolute.Should().Be(0m);

        var cursor = new OperationalRegisterDimensionResourceNetCursor(
            AfterPositiveGroup: false,
            AfterValueId: Guid.CreateVersion7(),
            NextOffset: 4,
            Total: 7,
            TotalPositive: 12m,
            TotalNegativeAbsolute: 3m);
        var continuation = await snapshotReader.GetResourceBalancesByDimensionCursorAsync(
            RegisterId,
            new DateOnly(2026, 8, 1),
            dimensions: null,
            groupDimensionId,
            "amount",
            cursor,
            limit: 10);
        continuation.Rows.Should().BeEmpty();
        continuation.Total.Should().Be(7);
        continuation.TotalPositive.Should().Be(12m);
        continuation.TotalNegativeAbsolute.Should().Be(3m);

        var probeCount = 0;
        var movementOnlyConnection = new RecordingDbConnection(
            readerFactory: _ => GroupNetRowsEmpty(),
            scalar: _ => Interlocked.Increment(ref probeCount) == 1);
        var movementOnlyReader = new PostgresOperationalRegisterMovementsQueryReader(
            new RecordingUnitOfWork(movementOnlyConnection),
            dependencies.Registers.Object,
            dependencies.Resources.Object,
            Mock.Of<IDimensionSetReader>(MockBehavior.Strict),
            enrichment.Object);
        var movementOnly = await movementOnlyReader.GetResourceBalancesByDimensionCursorAsync(
            RegisterId,
            new DateOnly(2026, 8, 1),
            [new DimensionValue(Guid.CreateVersion7(), Guid.CreateVersion7())],
            groupDimensionId,
            "amount",
            cursor: null,
            limit: 10);

        movementOnly.Rows.Should().BeEmpty();
        movementOnlyConnection.Commands.Last().CommandText.Should()
            .Contain("matching_dimension_sets")
            .And.Contain("FROM opreg_sales__movements movement")
            .And.NotContain("latest_snapshot");
        enrichment.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Movements_query_reader_rechecks_cached_context_after_transaction_change_and_rejects_missing_register()
    {
        var dependencies = RegisterDependencies([]);
        var relationExists = true;
        var connection = new RecordingDbConnection(
            scalar: sql => sql.Contains("to_regclass", StringComparison.Ordinal)
                ? relationExists
                : new DateOnly(2026, 8, 1));
        connection.Open();
        var firstTransaction = new RecordingDbTransaction(connection);
        var uow = new RecordingUnitOfWork(connection, hasActiveTransaction: true, firstTransaction);
        var relationCache = new NGB.PostgreSql.Schema.PostgresRelationPresenceCache(TimeProvider.System);
        var reader = new PostgresOperationalRegisterMovementsQueryReader(
            uow,
            dependencies.Registers.Object,
            dependencies.Resources.Object,
            Mock.Of<IDimensionSetReader>(MockBehavior.Strict),
            Mock.Of<IDimensionValueEnrichmentReader>(MockBehavior.Strict),
            new OperationalRegisterMetadataCache(TimeProvider.System),
            relationCache);

        (await reader.GetMaxPeriodMonthAsync(RegisterId)).Should().Be(new DateOnly(2026, 8, 1));

        uow.Transaction = new RecordingDbTransaction(connection);
        relationCache.Invalidate("opreg_sales__movements");
        relationExists = false;
        (await reader.GetMaxPeriodMonthAsync(RegisterId)).Should().BeNull();

        var missingConnection = new RecordingDbConnection(scalar: _ => false);
        var missingUow = new RecordingUnitOfWork(missingConnection);
        var missingRegister = new Mock<IOperationalRegisterRepository>(MockBehavior.Strict);
        missingRegister.Setup(x => x.GetByIdAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationalRegisterAdminItem?)null);
        var missingReader = new PostgresOperationalRegisterMovementsQueryReader(
            missingUow,
            missingRegister.Object,
            Mock.Of<IOperationalRegisterResourceRepository>(MockBehavior.Strict),
            Mock.Of<IDimensionSetReader>(MockBehavior.Strict),
            Mock.Of<IDimensionValueEnrichmentReader>(MockBehavior.Strict));

        await ((Func<Task>)(() => missingReader.GetMaxPeriodMonthAsync(RegisterId)))
            .Should().ThrowAsync<OperationalRegisterNotFoundException>();
    }

    [Fact]
    public async Task Movements_reader_returns_empty_for_absent_table_and_maps_database_null_resource_to_zero()
    {
        var dependencies = RegisterDependencies(
        [
            new("Quantity", "quantity", "quantity", "Quantity", 2),
            new("Amount", "amount", "amount", "Amount", 1)
        ]);
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
                readerFactory: sql => sql.Contains("SELECT DISTINCT", StringComparison.Ordinal)
                    ? MonthRows(month)
                    : MovementRows(documentId, occurredAt),
                scalar: _ => true)),
            dependencies.Registers.Object,
            dependencies.Resources.Object,
            new OperationalRegisterMetadataCache(TimeProvider.System),
            new PostgresRelationPresenceCache(TimeProvider.System));

        var row = (await present.GetByMonthAsync(RegisterId, month)).Should().ContainSingle().Subject;
        row.MovementId.Should().Be(9);
        row.DocumentId.Should().Be(documentId);
        row.OccurredAtUtc.Should().Be(occurredAt);
        row.DimensionSetId.Should().Be(DimensionSetId);
        row.IsStorno.Should().BeFalse();
        row.Resources.Should().Contain("amount", 0m);
        (await present.GetDistinctMonthsByDocumentAsync(RegisterId, documentId)).Should().Equal(month);

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

        var missingRegister = new Mock<IOperationalRegisterRepository>(MockBehavior.Strict);
        missingRegister.Setup(x => x.GetByIdAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationalRegisterAdminItem?)null);
        var missing = new PostgresOperationalRegisterMovementsReader(
            new RecordingUnitOfWork(new RecordingDbConnection()),
            missingRegister.Object,
            Mock.Of<IOperationalRegisterResourceRepository>(MockBehavior.Strict));
        await ((Func<Task>)(() => missing.GetByMonthAsync(RegisterId, month)))
            .Should().ThrowAsync<OperationalRegisterNotFoundException>();
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

        var nonZeroDependencies = RegisterDependencies(
        [
            new("Quantity", "quantity", "quantity", "Quantity", 2),
            new("Amount", "amount", "amount", "Amount", 1)
        ]);
        var nonZero = new PostgresOperationalRegisterMonthlyProjectionAggregator(
            new RecordingUnitOfWork(new RecordingDbConnection(
                readerFactory: _ => ProjectionRows(7.5m),
                scalar: _ => true)),
            nonZeroDependencies.Registers.Object,
            nonZeroDependencies.Resources.Object);
        (await nonZero.AggregateMonthAsync(RegisterId, new DateOnly(2026, 8, 1))).Should()
            .ContainSingle().Which.Values.Should().Contain("amount", 7.5m);

        var absentDependencies = RegisterDependencies([]);
        var absent = new PostgresOperationalRegisterMonthlyProjectionAggregator(
            new RecordingUnitOfWork(new RecordingDbConnection(scalar: _ => false)),
            absentDependencies.Registers.Object,
            absentDependencies.Resources.Object);
        (await absent.AggregateMonthAsync(RegisterId, new DateOnly(2026, 8, 1))).Should().BeEmpty();

        var missingRegister = new Mock<IOperationalRegisterRepository>(MockBehavior.Strict);
        missingRegister.Setup(x => x.GetByIdAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationalRegisterAdminItem?)null);
        var missing = new PostgresOperationalRegisterMonthlyProjectionAggregator(
            new RecordingUnitOfWork(new RecordingDbConnection()),
            missingRegister.Object,
            Mock.Of<IOperationalRegisterResourceRepository>(MockBehavior.Strict),
            new OperationalRegisterMetadataCache(TimeProvider.System),
            new PostgresRelationPresenceCache(TimeProvider.System));
        await ((Func<Task>)(() => missing.AggregateMonthAsync(RegisterId, new DateOnly(2026, 8, 1))))
            .Should().ThrowAsync<OperationalRegisterNotFoundException>();
    }

    [Fact]
    public async Task Balance_and_turnover_readers_accept_shared_caches_and_sort_resource_metadata()
    {
        var resourceRows = new[]
        {
            new OperationalRegisterResource("Quantity", "quantity", "quantity", "Quantity", 2),
            new OperationalRegisterResource("Amount", "amount", "amount", "Amount", 1)
        };
        var dimensions = new Mock<IDimensionSetReader>(MockBehavior.Strict);
        dimensions.Setup(x => x.GetBagsByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DimensionBag>());
        var enrichment = new Mock<IDimensionValueEnrichmentReader>(MockBehavior.Strict);
        var month = new DateOnly(2026, 8, 1);

        var balanceDependencies = RegisterDependencies(resourceRows);
        var balance = new PostgresOperationalRegisterBalancesReader(
            new RecordingUnitOfWork(new RecordingDbConnection(
                readerFactory: _ => MonthlyProjectionRows(month),
                scalar: _ => true)),
            balanceDependencies.Registers.Object,
            balanceDependencies.Resources.Object,
            dimensions.Object,
            enrichment.Object,
            new OperationalRegisterMetadataCache(TimeProvider.System),
            new PostgresRelationPresenceCache(TimeProvider.System));

        var balanceRow = (await balance.GetByMonthsAsync(RegisterId, month, month))
            .Should().ContainSingle().Subject;
        balanceRow.Values.Should().Contain("amount", 7.5m).And.Contain("quantity", 0m);

        var turnoverDependencies = RegisterDependencies(resourceRows);
        var turnover = new PostgresOperationalRegisterTurnoversReader(
            new RecordingUnitOfWork(new RecordingDbConnection(
                readerFactory: _ => MonthlyProjectionRows(month),
                scalar: _ => true)),
            turnoverDependencies.Registers.Object,
            turnoverDependencies.Resources.Object,
            dimensions.Object,
            enrichment.Object,
            new OperationalRegisterMetadataCache(TimeProvider.System),
            new PostgresRelationPresenceCache(TimeProvider.System));

        var turnoverRow = (await turnover.GetByMonthsAsync(RegisterId, month, month))
            .Should().ContainSingle().Subject;
        turnoverRow.Values.Should().Contain("amount", 7.5m).And.Contain("quantity", 0m);
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

    private static DataTableReader GroupNetRows(
        Guid valueId,
        decimal netAmount,
        int total,
        decimal totalPositive,
        decimal totalNegativeAbsolute)
    {
        var table = new DataTable();
        table.Columns.Add("ValueId", typeof(Guid));
        table.Columns.Add("NetAmount", typeof(decimal));
        table.Columns.Add("TotalCount", typeof(int));
        table.Columns.Add("TotalPositive", typeof(decimal));
        table.Columns.Add("TotalNegativeAbsolute", typeof(decimal));
        table.Rows.Add(valueId, netAmount, total, totalPositive, totalNegativeAbsolute);
        return table.CreateDataReader();
    }

    private static DataTableReader GroupNetRowsEmpty()
    {
        var table = new DataTable();
        table.Columns.Add("ValueId", typeof(Guid));
        table.Columns.Add("NetAmount", typeof(decimal));
        table.Columns.Add("TotalCount", typeof(int));
        table.Columns.Add("TotalPositive", typeof(decimal));
        table.Columns.Add("TotalNegativeAbsolute", typeof(decimal));
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

    private static DataTableReader MovementPageRows(Guid documentId, DateTime occurredAt, int total)
    {
        var table = new DataTable();
        table.Columns.Add("MovementId", typeof(long));
        table.Columns.Add("DocumentId", typeof(Guid));
        table.Columns.Add("OccurredAtUtc", typeof(DateTime));
        table.Columns.Add("PeriodMonth", typeof(DateOnly));
        table.Columns.Add("DimensionSetId", typeof(Guid));
        table.Columns.Add("IsStorno", typeof(bool));
        table.Columns.Add("TotalCount", typeof(long));
        table.Rows.Add(7L, documentId, occurredAt, new DateOnly(2026, 8, 1), DimensionSetId, false, (long)total);
        return table.CreateDataReader();
    }

    private static DataTableReader EmptyMovementPageRows()
    {
        var table = new DataTable();
        table.Columns.Add("MovementId", typeof(long));
        table.Columns.Add("DocumentId", typeof(Guid));
        table.Columns.Add("OccurredAtUtc", typeof(DateTime));
        table.Columns.Add("PeriodMonth", typeof(DateOnly));
        table.Columns.Add("DimensionSetId", typeof(Guid));
        table.Columns.Add("IsStorno", typeof(bool));
        table.Columns.Add("TotalCount", typeof(long));
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

    private static DataTableReader MonthRows(DateOnly month)
    {
        var table = new DataTable();
        table.Columns.Add("Month", typeof(DateOnly));
        table.Rows.Add(month);
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

    private static DataTableReader MonthlyProjectionRows(DateOnly month)
    {
        var table = new DataTable();
        table.Columns.Add("PeriodMonth", typeof(DateOnly));
        table.Columns.Add("DimensionSetId", typeof(Guid));
        table.Columns.Add("amount", typeof(decimal));
        table.Rows.Add(month, DimensionSetId, 7.5m);
        return table.CreateDataReader();
    }

    private static DataTableReader ResourceNetRequestRows()
    {
        var table = new DataTable();
        table.Columns.Add("RequestIndex", typeof(int));
        table.Columns.Add("NetAmount", typeof(decimal));
        table.Rows.Add(0, 5m);
        table.Rows.Add(1, -2m);
        return table.CreateDataReader();
    }

    private static DataTableReader ResourceNetBySetRows(params (Guid Id, decimal Net)[] rows)
    {
        var table = new DataTable();
        table.Columns.Add("DimensionSetId", typeof(Guid));
        table.Columns.Add("NetAmount", typeof(decimal));
        foreach (var row in rows)
            table.Rows.Add(row.Id, row.Net);
        return table.CreateDataReader();
    }
}
