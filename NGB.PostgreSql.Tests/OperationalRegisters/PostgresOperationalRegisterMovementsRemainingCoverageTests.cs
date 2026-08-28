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
        await missingAggregateRegister.Should().ThrowAsync<NgbArgumentRequiredException>();
        await missingGroupDimension.Should().ThrowAsync<NgbArgumentRequiredException>();
        await missingResource.Should().ThrowAsync<NgbArgumentRequiredException>();
        await reversedAggregate.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await aggregateNonMonth.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await zeroLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await reversed.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await invalidFrom.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await invalidTo.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
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
        registers.Verify(x => x.GetByIdAsync(registerId, It.IsAny<CancellationToken>()), Times.Exactly(4));
        resources.Verify(x => x.GetByRegisterIdAsync(registerId, It.IsAny<CancellationToken>()), Times.Exactly(4));

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
            .And.Contain("WHERE is_storno = FALSE");
        schemaCommand.Split("CREATE INDEX IF NOT EXISTS", StringSplitOptions.None).Should().HaveCount(8);
        await resourceStore.AppendStornoByDocumentAsync(registerId, documentId);
    }

    private static OperationalRegisterMovement Movement(IReadOnlyDictionary<string, decimal> resources)
        => new(
            Guid.NewGuid(),
            new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc),
            Guid.Empty,
            resources);

    private static OperationalRegisterAdminItem Register(Guid id)
        => new(id, "Sales", "sales", "sales", "Sales", false, DateTime.UnixEpoch, DateTime.UnixEpoch);
}
