using FluentAssertions;
using Moq;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.OperationalRegisters;
using NGB.PostgreSql.OperationalRegisters;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.OperationalRegisters;

public sealed class PostgresOperationalRegisterMovementsRemainingCoverageTests
{
    [Fact]
    public async Task Query_reader_rejects_invalid_identifiers_limits_ranges_and_month_boundaries()
    {
        var sut = new PostgresOperationalRegisterMovementsQueryReader(null!, null!, null!, null!, null!);
        var jan = new DateOnly(2026, 1, 1);

        Func<Task> missingMaxRegister = async () => await sut.GetMaxPeriodMonthAsync(Guid.Empty);
        Func<Task> missingRegister = async () => await sut.GetByMonthsAsync(Guid.Empty, jan, jan);
        Func<Task> missingPagedRegister = async () => await sut.GetByOccurredAtPageAsync(Guid.Empty, jan, jan);
        Func<Task> reversedPage = async () => await sut.GetByOccurredAtPageAsync(
            Guid.NewGuid(), new DateOnly(2026, 2, 1), jan);
        Func<Task> negativeOffset = async () => await sut.GetByOccurredAtPageAsync(Guid.NewGuid(), jan, jan, offset: -1);
        Func<Task> zeroPageLimit = async () => await sut.GetByOccurredAtPageAsync(Guid.NewGuid(), jan, jan, limit: 0);
        Func<Task> missingCursorRegister = async () => await sut.GetByOccurredAtCursorAsync(Guid.Empty, jan, jan);
        Func<Task> reversedCursorRange = async () => await sut.GetByOccurredAtCursorAsync(
            Guid.NewGuid(), new DateOnly(2026, 2, 1), jan);
        Func<Task> zeroCursorLimit = async () => await sut.GetByOccurredAtCursorAsync(Guid.NewGuid(), jan, jan, limit: 0);
        Func<Task> nonUtcCursor = async () => await sut.GetByOccurredAtCursorAsync(
            Guid.NewGuid(), jan, jan, cursor: new OperationalRegisterOccurredAtCursor(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Local), 1));
        Func<Task> invalidCursorId = async () => await sut.GetByOccurredAtCursorAsync(
            Guid.NewGuid(), jan, jan, cursor: new OperationalRegisterOccurredAtCursor(DateTime.UtcNow, 0));
        Func<Task> missingAggregateRegister = async () => await sut.GetResourceNetsByDimensionAsync(
            Guid.Empty, jan, jan, null, Guid.NewGuid(), "amount");
        Func<Task> missingGroupDimension = async () => await sut.GetResourceNetsByDimensionAsync(
            Guid.NewGuid(), jan, jan, null, Guid.Empty, "amount");
        Func<Task> missingResource = async () => await sut.GetResourceNetsByDimensionAsync(
            Guid.NewGuid(), jan, jan, null, Guid.NewGuid(), " ");
        Func<Task> reversedAggregate = async () => await sut.GetResourceNetsByDimensionAsync(
            Guid.NewGuid(), new DateOnly(2026, 2, 1), jan, null, Guid.NewGuid(), "amount");
        Func<Task> aggregateNonMonth = async () => await sut.GetResourceNetsByDimensionAsync(
            Guid.NewGuid(), new DateOnly(2026, 1, 2), new DateOnly(2026, 2, 1), null, Guid.NewGuid(), "amount");
        Func<Task> negativeAggregateOffset = async () => await sut.GetResourceNetsByDimensionPageAsync(
            Guid.NewGuid(), jan, jan, null, Guid.NewGuid(), "amount", -1, 1);
        Func<Task> zeroAggregateLimit = async () => await sut.GetResourceNetsByDimensionPageAsync(
            Guid.NewGuid(), jan, jan, null, Guid.NewGuid(), "amount", 0, 0);
        Func<Task> missingBalanceRegister = async () => await sut.GetResourceBalancesByDimensionPageAsync(
            Guid.Empty, jan, null, Guid.NewGuid(), "amount", 0, 1);
        Func<Task> missingBalanceDimension = async () => await sut.GetResourceBalancesByDimensionPageAsync(
            Guid.NewGuid(), jan, null, Guid.Empty, "amount", 0, 1);
        Func<Task> missingBalanceResource = async () => await sut.GetResourceBalancesByDimensionPageAsync(
            Guid.NewGuid(), jan, null, Guid.NewGuid(), " ", 0, 1);
        Func<Task> negativeBalanceOffset = async () => await sut.GetResourceBalancesByDimensionPageAsync(
            Guid.NewGuid(), jan, null, Guid.NewGuid(), "amount", -1, 1);
        Func<Task> zeroBalanceLimit = async () => await sut.GetResourceBalancesByDimensionPageAsync(
            Guid.NewGuid(), jan, null, Guid.NewGuid(), "amount", 0, 0);
        Func<Task> missingBalanceCursorRegister = async () => await sut.GetResourceBalancesByDimensionCursorAsync(
            Guid.Empty, jan, null, Guid.NewGuid(), "amount", null, 1);
        Func<Task> missingBalanceCursorDimension = async () => await sut.GetResourceBalancesByDimensionCursorAsync(
            Guid.NewGuid(), jan, null, Guid.Empty, "amount", null, 1);
        Func<Task> missingBalanceCursorResource = async () => await sut.GetResourceBalancesByDimensionCursorAsync(
            Guid.NewGuid(), jan, null, Guid.NewGuid(), " ", null, 1);
        Func<Task> zeroBalanceCursorLimit = async () => await sut.GetResourceBalancesByDimensionCursorAsync(
            Guid.NewGuid(), jan, null, Guid.NewGuid(), "amount", null, 0);
        Func<Task> zeroLimit = async () => await sut.GetByMonthsAsync(Guid.NewGuid(), jan, jan, limit: 0);
        Func<Task> reversed = async () => await sut.GetByMonthsAsync(
            Guid.NewGuid(), new DateOnly(2026, 2, 1), jan);
        Func<Task> invalidFrom = async () => await sut.GetByMonthsAsync(
            Guid.NewGuid(), new DateOnly(2026, 1, 2), new DateOnly(2026, 2, 1));
        Func<Task> invalidTo = async () => await sut.GetByMonthsAsync(
            Guid.NewGuid(), jan, new DateOnly(2026, 2, 2));

        await missingMaxRegister.Should().ThrowAsync<NgbArgumentRequiredException>();
        await missingRegister.Should().ThrowAsync<NgbArgumentRequiredException>();
        await missingPagedRegister.Should().ThrowAsync<NgbArgumentRequiredException>();
        await reversedPage.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await negativeOffset.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await zeroPageLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await missingCursorRegister.Should().ThrowAsync<NgbArgumentRequiredException>();
        await reversedCursorRange.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await zeroCursorLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await nonUtcCursor.Should().ThrowAsync<NgbArgumentInvalidException>();
        await invalidCursorId.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await missingAggregateRegister.Should().ThrowAsync<NgbArgumentRequiredException>();
        await missingGroupDimension.Should().ThrowAsync<NgbArgumentRequiredException>();
        await missingResource.Should().ThrowAsync<NgbArgumentRequiredException>();
        await reversedAggregate.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await aggregateNonMonth.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await negativeAggregateOffset.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await zeroAggregateLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await missingBalanceRegister.Should().ThrowAsync<NgbArgumentRequiredException>();
        await missingBalanceDimension.Should().ThrowAsync<NgbArgumentRequiredException>();
        await missingBalanceResource.Should().ThrowAsync<NgbArgumentRequiredException>();
        await negativeBalanceOffset.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await zeroBalanceLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await missingBalanceCursorRegister.Should().ThrowAsync<NgbArgumentRequiredException>();
        await missingBalanceCursorDimension.Should().ThrowAsync<NgbArgumentRequiredException>();
        await missingBalanceCursorResource.Should().ThrowAsync<NgbArgumentRequiredException>();
        await zeroBalanceCursorLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await zeroLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await reversed.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await invalidFrom.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await invalidTo.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Query_reader_returns_empty_for_absent_tables_and_preserves_cursor_totals_for_empty_tail()
    {
        var registerId = Guid.NewGuid();
        var groupDimensionId = Guid.NewGuid();
        var registers = new Mock<IOperationalRegisterRepository>(MockBehavior.Strict);
        registers.Setup(x => x.GetByIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register(registerId));
        var resources = new Mock<IOperationalRegisterResourceRepository>(MockBehavior.Strict);
        resources.Setup(x => x.GetByRegisterIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new OperationalRegisterResource("Other", "other", "other", "Other", 2),
                new OperationalRegisterResource("Amount", "amount", "amount", "Amount", 1)
            ]);
        var absent = new PostgresOperationalRegisterMovementsQueryReader(
            new RecordingUnitOfWork(new RecordingDbConnection(scalar: _ => false)),
            registers.Object,
            resources.Object,
            Mock.Of<NGB.Persistence.Dimensions.IDimensionSetReader>(MockBehavior.Strict),
            Mock.Of<NGB.Persistence.Dimensions.Enrichment.IDimensionValueEnrichmentReader>(MockBehavior.Strict));
        var month = new DateOnly(2026, 8, 1);

        (await absent.GetByOccurredAtCursorAsync(registerId, month, month)).Should().BeEmpty();
        (await absent.GetByOccurredAtPageAsync(registerId, month, month))
            .Should().Be(new OperationalRegisterMovementQueryPage([], 0));
        (await absent.GetResourceBalancesByDimensionPageAsync(
                registerId, month, null, groupDimensionId, "amount", 0, 1))
            .Should().Be(new OperationalRegisterDimensionResourceNetPage([], 0, 0m, 0m));
        (await absent.GetResourceBalancesByDimensionCursorAsync(
                registerId, month, null, groupDimensionId, "amount", null, 1))
            .Should().Be(new OperationalRegisterDimensionResourceNetPage([], 0, 0m, 0m));

        var presentRegisters = new Mock<IOperationalRegisterRepository>(MockBehavior.Strict);
        presentRegisters.Setup(x => x.GetByIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register(registerId));
        var presentResources = new Mock<IOperationalRegisterResourceRepository>(MockBehavior.Strict);
        presentResources.Setup(x => x.GetByRegisterIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new OperationalRegisterResource("Amount", "amount", "amount", "Amount", 1)]);
        var present = new PostgresOperationalRegisterMovementsQueryReader(
            new RecordingUnitOfWork(new RecordingDbConnection(scalar: _ => true)),
            presentRegisters.Object,
            presentResources.Object,
            Mock.Of<NGB.Persistence.Dimensions.IDimensionSetReader>(MockBehavior.Strict),
            Mock.Of<NGB.Persistence.Dimensions.Enrichment.IDimensionValueEnrichmentReader>(MockBehavior.Strict));

        await ((Func<Task>)(() => present.GetResourceBalancesByDimensionPageAsync(
                registerId, month, null, groupDimensionId, "missing", 0, 1)))
            .Should().ThrowAsync<NgbConfigurationViolationException>();
        await ((Func<Task>)(() => present.GetResourceBalancesByDimensionCursorAsync(
                registerId, month, null, groupDimensionId, "missing", null, 1)))
            .Should().ThrowAsync<NgbConfigurationViolationException>();

        var carriedCursor = new OperationalRegisterDimensionResourceNetCursor(
            AfterPositiveGroup: false,
            AfterValueId: Guid.NewGuid(),
            NextOffset: 5,
            Total: 9,
            TotalPositive: 12m,
            TotalNegativeAbsolute: 3m);
        var emptyTail = await present.GetResourceBalancesByDimensionCursorAsync(
            registerId,
            month,
            null,
            groupDimensionId,
            "amount",
            carriedCursor,
            1);
        emptyTail.Should().Be(new OperationalRegisterDimensionResourceNetPage([], 9, 12m, 3m));
    }

    [Fact]
    public void Max_period_scalar_and_dimension_filter_sql_cover_all_database_shapes()
    {
        var date = new DateOnly(2026, 8, 1);
        PostgresOperationalRegisterMovementsQueryReader.ConvertMaxPeriodMonthScalar(null).Should().BeNull();
        PostgresOperationalRegisterMovementsQueryReader.ConvertMaxPeriodMonthScalar(DBNull.Value).Should().BeNull();
        PostgresOperationalRegisterMovementsQueryReader.ConvertMaxPeriodMonthScalar(date).Should().Be(date);
        PostgresOperationalRegisterMovementsQueryReader.ConvertMaxPeriodMonthScalar(
            new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc)).Should().Be(date);

        Action unexpected = () => PostgresOperationalRegisterMovementsQueryReader.ConvertMaxPeriodMonthScalar(42);
        unexpected.Should().Throw<NgbUnexpectedException>()
            .Which.Context.Should().Contain("scalarType", typeof(int).FullName);

        PostgresOperationalRegisterMovementsQueryReader.BuildDimensionFilterCte(0).Should().BeEmpty();
        PostgresOperationalRegisterMovementsQueryReader.BuildDimensionFilterCte(2).Should()
            .Contain("matching_dimension_sets").And.Contain("@DimCount");
        PostgresOperationalRegisterMovementsQueryReader.BuildDimensionFilterSql("m", 0).Should().BeEmpty();
        PostgresOperationalRegisterMovementsQueryReader.BuildDimensionFilterSql("m", 2).Should()
            .Be("AND m.dimension_set_id IN (SELECT dimension_set_id FROM matching_dimension_sets)");
    }

    [Fact]
    public void Movement_resource_validation_and_array_projection_cover_empty_null_unknown_missing_and_present_values()
    {
        var registerId = Guid.NewGuid();
        var resource = new OperationalRegisterResource("Amount", "amount", "amount", "Amount", 1);
        var movement = Movement(new Dictionary<string, decimal> { ["amount"] = 12.5m });
        var missingValue = Movement(new Dictionary<string, decimal>());

        PostgresOperationalRegisterMovementsStore.ValidateResourceKeys(registerId, [], []);
        PostgresOperationalRegisterMovementsStore.ValidateResourceKeys(registerId, [], [missingValue]);
        PostgresOperationalRegisterMovementsStore.ValidateResourceKeys(registerId, [resource], [movement, missingValue]);

        Action nullWithoutDefinitions = () => PostgresOperationalRegisterMovementsStore.ValidateResourceKeys(
            registerId, [], [Movement(null!)]);
        Action unknownWithoutDefinitions = () => PostgresOperationalRegisterMovementsStore.ValidateResourceKeys(
            registerId, [], [Movement(new Dictionary<string, decimal> { ["unknown"] = 1m })]);
        Action nullWithDefinitions = () => PostgresOperationalRegisterMovementsStore.ValidateResourceKeys(
            registerId, [resource], [Movement(null!)]);
        Action unknownWithDefinitions = () => PostgresOperationalRegisterMovementsStore.ValidateResourceKeys(
            registerId, [resource], [Movement(new Dictionary<string, decimal> { ["unknown"] = 1m })]);

        nullWithoutDefinitions.Should().Throw<NgbArgumentInvalidException>();
        var noDefinitionError = unknownWithoutDefinitions.Should()
            .Throw<OperationalRegisterResourcesValidationException>().Which;
        noDefinitionError.Context.Should().Contain("movementIndex", 0)
            .And.Contain("unknownKey", "unknown");
        nullWithDefinitions.Should().Throw<NgbArgumentInvalidException>();
        unknownWithDefinitions.Should().Throw<OperationalRegisterResourcesValidationException>();

        var arrays = PostgresOperationalRegisterMovementsStore.BuildResourceArrays(
            [resource],
            [movement, missingValue]);
        arrays.Should().ContainSingle();
        arrays[0].ParamName.Should().Be("p_amount");
        arrays[0].Values.Should().Equal(12.5m, 0m);
    }

    [Fact]
    public async Task Movement_store_validates_batches_and_reports_missing_registers_in_every_operation()
    {
        var registers = new Mock<IOperationalRegisterRepository>(MockBehavior.Strict);
        registers.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationalRegisterAdminItem?)null);
        var resources = new Mock<IOperationalRegisterResourceRepository>(MockBehavior.Strict);
        var connection = new RecordingDbConnection();
        var sut = new PostgresOperationalRegisterMovementsStore(
            new RecordingUnitOfWork(connection, hasActiveTransaction: true),
            registers.Object,
            resources.Object);
        var registerId = Guid.NewGuid();

        Func<Task> nullBatch = () => sut.AppendAsync(registerId, null!);
        await nullBatch.Should().ThrowAsync<NgbArgumentRequiredException>();

        await sut.AppendAsync(registerId, []);
        connection.Commands.Should().BeEmpty();

        Func<Task> ensureMissing = () => sut.EnsureSchemaAsync(registerId);
        Func<Task> appendMissing = () => sut.AppendAsync(registerId, [Movement(new Dictionary<string, decimal>())]);
        Func<Task> stornoMissing = () => sut.AppendStornoByDocumentAsync(registerId, Guid.NewGuid());

        await ensureMissing.Should().ThrowAsync<OperationalRegisterNotFoundException>();
        await appendMissing.Should().ThrowAsync<OperationalRegisterNotFoundException>();
        await stornoMissing.Should().ThrowAsync<OperationalRegisterNotFoundException>();
        registers.Verify(x => x.GetByIdAsync(registerId, It.IsAny<CancellationToken>()), Times.Exactly(3));
        resources.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Movement_store_rejects_empty_document_and_supports_storno_without_resources()
    {
        var registerId = Guid.NewGuid();
        var registers = new Mock<IOperationalRegisterRepository>(MockBehavior.Strict);
        registers.Setup(x => x.GetByIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register(registerId));
        var resources = new Mock<IOperationalRegisterResourceRepository>(MockBehavior.Strict);
        resources.Setup(x => x.GetByRegisterIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var connection = new RecordingDbConnection();
        var sut = new PostgresOperationalRegisterMovementsStore(
            new RecordingUnitOfWork(connection, hasActiveTransaction: true),
            registers.Object,
            resources.Object);

        Func<Task> emptyDocument = () => sut.AppendAsync(
            registerId,
            [new OperationalRegisterMovement(Guid.Empty, DateTime.UnixEpoch, Guid.Empty, new Dictionary<string, decimal>())]);
        await emptyDocument.Should().ThrowAsync<NgbArgumentInvalidException>();

        await sut.EnsureSchemaAsync(registerId);

        var validDocumentId = Guid.NewGuid();
        await sut.AppendAsync(registerId,
        [
            new OperationalRegisterMovement(
                validDocumentId,
                DateTime.UnixEpoch,
                Guid.Empty,
                new Dictionary<string, decimal>())
        ]);

        var documentId = Guid.NewGuid();
        await sut.AppendStornoByDocumentAsync(registerId, documentId);

        connection.Commands.Should().Contain(x => x.CommandText.Contains(
            "INSERT INTO opreg_", StringComparison.Ordinal) &&
            x.CommandText.Contains("is_storno)", StringComparison.Ordinal));
        registers.Verify(x => x.GetByIdAsync(registerId, It.IsAny<CancellationToken>()), Times.Once);
        resources.Verify(x => x.GetByRegisterIdAsync(registerId, It.IsAny<CancellationToken>()), Times.Once);
        connection.Commands.Count(command => command.CommandText.Contains(
                "UPDATE operational_registers SET has_movements = TRUE",
                StringComparison.Ordinal))
            .Should().Be(1);

        var resource = new OperationalRegisterResource("Amount", "amount", "amount", "Amount", 1);
        var resourceRegisters = new Mock<IOperationalRegisterRepository>(MockBehavior.Strict);
        resourceRegisters.Setup(x => x.GetByIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register(registerId));
        var resourceDefinitions = new Mock<IOperationalRegisterResourceRepository>(MockBehavior.Strict);
        resourceDefinitions.Setup(x => x.GetByRegisterIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([resource]);
        var resourceConnection = new RecordingDbConnection();
        var resourceStore = new PostgresOperationalRegisterMovementsStore(
            new RecordingUnitOfWork(resourceConnection, hasActiveTransaction: true),
            resourceRegisters.Object,
            resourceDefinitions.Object);
        await resourceStore.EnsureSchemaAsync(registerId);
        var schemaCommand = resourceConnection.Commands
            .Should().ContainSingle(command => command.CommandText.Contains("CREATE TABLE IF NOT EXISTS", StringComparison.Ordinal))
            .Which.CommandText;
        schemaCommand.Should()
            .Contain("ADD COLUMN IF NOT EXISTS amount")
            .And.Contain("CREATE TRIGGER")
            .And.Contain("WHERE is_storno = FALSE")
            .And.Contain("(occurred_at_utc, movement_id)");
        schemaCommand.Split("CREATE INDEX IF NOT EXISTS", StringSplitOptions.None).Should().HaveCount(9);
        await resourceStore.AppendStornoByDocumentAsync(registerId, documentId);
    }

    [Fact]
    public async Task Movement_store_skips_schema_ddl_when_durable_metadata_proves_write_readiness()
    {
        var registerId = Guid.NewGuid();
        var registers = new Mock<IOperationalRegisterRepository>(MockBehavior.Strict);
        registers.Setup(x => x.GetByIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register(registerId, hasMovements: true));
        var resources = new Mock<IOperationalRegisterResourceRepository>(MockBehavior.Strict);
        resources.Setup(x => x.GetByRegisterIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var connection = new RecordingDbConnection(scalar: _ => true);
        var sut = new PostgresOperationalRegisterMovementsStore(
            new RecordingUnitOfWork(connection, hasActiveTransaction: true),
            registers.Object,
            resources.Object);

        await sut.EnsureReadyForWriteAsync(registerId, default);

        connection.Commands.Should().ContainSingle(command =>
            command.CommandText.Contains("pg_attribute", StringComparison.Ordinal));
        resources.VerifyAll();
        registers.VerifyAll();
    }

    private static OperationalRegisterMovement Movement(IReadOnlyDictionary<string, decimal> resources)
        => new(
            Guid.NewGuid(),
            new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc),
            Guid.Empty,
            resources);

    private static OperationalRegisterAdminItem Register(Guid id, bool hasMovements = false)
        => new(id, "Sales", "sales", "sales", "Sales", hasMovements, DateTime.UnixEpoch, DateTime.UnixEpoch);
}
