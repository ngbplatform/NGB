using FluentAssertions;
using Moq;
using NGB.Core.Dimensions;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.OperationalRegisters.Projections;
using NGB.Runtime.OperationalRegisters.Projections.Examples;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.OperationalRegisters;

public sealed class OperationalRegisterReadAndProjectionFullCoverageTests
{
    private static readonly DateTime Now = new(2026, 8, 15, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ReadService_MovementsValidatesEveryBoundary()
    {
        var f = new ReadFixture();
        var id = Guid.NewGuid();
        var january = new DateOnly(2026, 1, 1);
        var february = new DateOnly(2026, 2, 1);

        await ((Func<Task>)(() => f.Sut.GetMovementsPageAsync(null!)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.GetMovementsPageAsync(new(Guid.Empty, january, february))))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.GetMovementsPageAsync(new(id, february, january))))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => f.Sut.GetMovementsPageAsync(new(id, january.AddDays(1), february))))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => f.Sut.GetMovementsPageAsync(new(id, january, february.AddDays(1)))))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ReadService_MovementsCoversMinMaxNormalPagingAndDimensionCanonicalization()
    {
        var f = new ReadFixture();
        var id = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var setId = Guid.NewGuid();
        var january = new DateOnly(2026, 1, 1);
        var dimensionId = Guid.NewGuid();
        var valueId = Guid.NewGuid();
        var dims = new[] { new DimensionValue(dimensionId, valueId), new DimensionValue(dimensionId, valueId) };
        var capturedLimits = new List<int>();
        IReadOnlyList<DimensionValue>? capturedDims = null;
        f.Movements.Setup(x => x.GetByMonthsAsync(id, january, january,
                It.IsAny<IReadOnlyList<DimensionValue>?>(), setId, documentId, true, 10, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, DateOnly, DateOnly, IReadOnlyList<DimensionValue>?, Guid?, Guid?, bool?, long?, int, CancellationToken>(
                (_, _, _, d, _, _, _, _, limit, _) => { capturedDims = d; capturedLimits.Add(limit); })
            .ReturnsAsync([MovementRow(11), MovementRow(12)]);

        var page = await f.Sut.GetMovementsPageAsync(new(id, january, january, dims, setId,
            documentId, true, new OperationalRegisterMovementsPageCursor(10), PageSize: 0));

        page.HasMore.Should().BeTrue();
        page.Lines.Should().ContainSingle().Which.MovementId.Should().Be(11);
        page.NextCursor!.AfterMovementId.Should().Be(11);
        capturedDims.Should().ContainSingle().Which.Should().Be(new DimensionValue(dimensionId, valueId));
        capturedLimits.Should().Equal(2);

        f.Movements.Setup(x => x.GetByMonthsAsync(id, january, january,
                It.IsAny<IReadOnlyList<DimensionValue>?>(), null, null, null, null, 5001, It.IsAny<CancellationToken>()))
            .Callback<Guid, DateOnly, DateOnly, IReadOnlyList<DimensionValue>?, Guid?, Guid?, bool?, long?, int, CancellationToken>(
                (_, _, _, d, _, _, _, _, limit, _) => { capturedDims = d; capturedLimits.Add(limit); })
            .ReturnsAsync([MovementRow(1)]);
        var max = await f.Sut.GetMovementsPageAsync(new(id, january, january, Dimensions: null, PageSize: 6000));
        max.HasMore.Should().BeFalse();
        max.NextCursor.Should().BeNull();
        capturedDims.Should().BeNull();
        capturedLimits.Should().Contain(5001);

        var emptyDims = Array.Empty<DimensionValue>();
        f.Movements.Setup(x => x.GetByMonthsAsync(id, january, january, emptyDims,
                null, null, null, null, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var normal = await f.Sut.GetMovementsPageAsync(new(id, january, january, Dimensions: emptyDims, PageSize: 2));
        normal.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadService_ProjectionMethodsValidateAndPageTurnoversAndBalances()
    {
        var f = new ReadFixture();
        var id = Guid.NewGuid();
        var january = new DateOnly(2026, 1, 1);
        var february = new DateOnly(2026, 2, 1);

        await ((Func<Task>)(() => f.Sut.GetTurnoversPageAsync(null!)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.GetBalancesPageAsync(new(Guid.Empty, january, february))))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.GetTurnoversPageAsync(new(id, february, january))))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => f.Sut.GetBalancesPageAsync(new(id, january.AddDays(1), february))))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => f.Sut.GetBalancesPageAsync(new(id, january, february.AddDays(1)))))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();

        var dimensionId = Guid.NewGuid();
        var value = new DimensionValue(dimensionId, Guid.NewGuid());
        var cursorSetId = Guid.NewGuid();
        var rows = new[] { ProjectionRead(january, Guid.NewGuid()), ProjectionRead(february, cursorSetId) };
        f.Turnovers.Setup(x => x.GetPageByMonthsAsync(id, january, february,
                It.Is<IReadOnlyList<DimensionValue>?>(d => d != null && d.Count == 1), null,
                january, cursorSetId, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

        var page = await f.Sut.GetTurnoversPageAsync(new(id, january, february, [value, value],
            Cursor: new OperationalRegisterMonthlyProjectionPageCursor(january, cursorSetId), PageSize: -5));
        page.HasMore.Should().BeTrue();
        page.Lines.Should().ContainSingle().Which.PeriodMonth.Should().Be(january);
        page.NextCursor!.AfterPeriodMonth.Should().Be(january);

        f.Balances.Setup(x => x.GetPageByMonthsAsync(id, january, february,
                null, null, null, null, 5001, It.IsAny<CancellationToken>()))
            .ReturnsAsync([ProjectionRead(february, cursorSetId)]);
        var balances = await f.Sut.GetBalancesPageAsync(new(id, january, february, PageSize: 5001));
        balances.HasMore.Should().BeFalse();
        balances.NextCursor.Should().BeNull();

        var emptyDimensions = Array.Empty<DimensionValue>();
        f.Balances.Setup(x => x.GetPageByMonthsAsync(id, january, january,
                emptyDimensions, null, null, null, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        (await f.Sut.GetBalancesPageAsync(new(id, january, january, emptyDimensions, PageSize: 2)))
            .Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task DefaultProjector_ValidatesContextAndBuildsCumulativeBalancesWithZeroFiltering()
    {
        var aggregator = new Mock<IOperationalRegisterMonthlyProjectionAggregator>(MockBehavior.Loose);
        var finalizations = new Mock<IOperationalRegisterFinalizationRepository>(MockBehavior.Loose);
        var turnovers = new Mock<IOperationalRegisterTurnoversStore>(MockBehavior.Loose);
        var balances = new Mock<IOperationalRegisterBalancesStore>(MockBehavior.Loose);
        var sut = new DefaultOperationalRegisterMonthProjector(
            aggregator.Object, finalizations.Object, turnovers.Object, balances.Object);
        var period = new DateOnly(2026, 4, 1);
        var previous = new DateOnly(2026, 3, 1);
        var id = Guid.NewGuid();
        var firstSet = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondSet = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var zeroSet = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var context = Context(id, period);

        await ((Func<Task>)(() => sut.RebuildMonthAsync(Context(Guid.Empty, period))))
            .Should().ThrowAsync<NgbArgumentRequiredException>();

        var turnoverRows = new[]
        {
            Projection(firstSet, ("qty", -3m), ("new", 2m)),
            Projection(secondSet, ("qty", 1m))
        };
        aggregator.Setup(x => x.AggregateMonthAsync(id, period, It.IsAny<CancellationToken>()))
            .ReturnsAsync(turnoverRows);
        finalizations.Setup(x => x.GetLatestFinalizedPeriodBeforeAsync(id, period, It.IsAny<CancellationToken>()))
            .ReturnsAsync(previous);
        balances.Setup(x => x.GetByMonthAsync(id, previous, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Projection(firstSet, ("qty", 1m)),
                Projection(firstSet, ("qty", 3m)),
                Projection(zeroSet, ("qty", 0m))
            ]);
        IReadOnlyList<OperationalRegisterMonthlyProjectionRow>? written = null;
        balances.Setup(x => x.ReplaceForMonthAsync(id, period,
                It.IsAny<IReadOnlyList<OperationalRegisterMonthlyProjectionRow>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, DateOnly, IReadOnlyList<OperationalRegisterMonthlyProjectionRow>, CancellationToken>(
                (_, _, rows, _) => written = rows)
            .Returns(Task.CompletedTask);

        await sut.RebuildMonthAsync(context);

        written.Should().HaveCount(2);
        written![0].DimensionSetId.Should().Be(firstSet);
        written[0].Values.Should().BeEquivalentTo(new Dictionary<string, decimal> { ["qty"] = 0m, ["new"] = 2m });
        written[1].DimensionSetId.Should().Be(secondSet);
        written[1].Values["qty"].Should().Be(1m);
        turnovers.Verify(x => x.ReplaceForMonthAsync(id, period, turnoverRows, It.IsAny<CancellationToken>()), Times.Once);

        finalizations.Setup(x => x.GetLatestFinalizedPeriodBeforeAsync(id, period, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateOnly?)null);
        aggregator.Setup(x => x.AggregateMonthAsync(id, period, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        await sut.RebuildMonthAsync(context);
        balances.Verify(x => x.GetByMonthAsync(id, previous, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DefaultProjector_DelegatesToOptimizedRebuilderAfterSchemaPreparation()
    {
        var id = Guid.CreateVersion7();
        var period = new DateOnly(2026, 4, 1);
        var previous = new DateOnly(2026, 3, 1);
        var aggregator = new Mock<IOperationalRegisterMonthlyProjectionAggregator>(MockBehavior.Strict);
        var finalizations = new Mock<IOperationalRegisterFinalizationRepository>(MockBehavior.Strict);
        var turnovers = new Mock<IOperationalRegisterTurnoversStore>(MockBehavior.Strict);
        var balances = new Mock<IOperationalRegisterBalancesStore>(MockBehavior.Strict);
        var optimized = new Mock<IOperationalRegisterDefaultProjectionRebuilder>(MockBehavior.Strict);
        turnovers.Setup(x => x.EnsureReadyForWriteAsync(id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        balances.Setup(x => x.EnsureReadyForWriteAsync(id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        finalizations.Setup(x => x.GetLatestFinalizedPeriodBeforeAsync(id, period, It.IsAny<CancellationToken>()))
            .ReturnsAsync(previous);
        optimized.Setup(x => x.RebuildMonthAsync(id, period, previous, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var sut = new DefaultOperationalRegisterMonthProjector(
            aggregator.Object,
            finalizations.Object,
            turnovers.Object,
            balances.Object,
            optimized.Object);

        await sut.RebuildMonthAsync(Context(id, period));

        optimized.VerifyAll();
        aggregator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MovementsCountProjector_ValidatesConstructorContextPagesAndNetCounts()
    {
        var turnovers = new Mock<IOperationalRegisterTurnoversStore>(MockBehavior.Loose);
        var balances = new Mock<IOperationalRegisterBalancesStore>(MockBehavior.Loose);
        ((Action)(() => new MovementsCountProjector(null!, turnovers.Object, balances.Object)))
            .Should().Throw<NgbArgumentRequiredException>();

        var sut = new MovementsCountProjector("  Stock_Count  ", turnovers.Object, balances.Object);
        sut.RegisterCodeNorm.Should().Be("stock_count");
        var period = new DateOnly(2026, 5, 1);
        await ((Func<Task>)(() => sut.RebuildMonthAsync(Context(Guid.Empty, period))))
            .Should().ThrowAsync<NgbArgumentRequiredException>();

        var registerId = Guid.NewGuid();
        var positive = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var zero = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var movements = new Mock<IOperationalRegisterMovementsReader>(MockBehavior.Loose);
        movements.SetupSequence(x => x.GetByMonthAsync(registerId, period, null,
                It.IsAny<long?>(), 2000, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MovementRead(1, positive, false),
                MovementRead(2, positive, false),
                MovementRead(3, positive, true),
                MovementRead(4, zero, false),
                MovementRead(5, zero, true)
            ])
            .ReturnsAsync([]);
        IReadOnlyList<OperationalRegisterMonthlyProjectionRow>? written = null;
        turnovers.Setup(x => x.ReplaceForMonthAsync(registerId, period,
                It.IsAny<IReadOnlyList<OperationalRegisterMonthlyProjectionRow>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, DateOnly, IReadOnlyList<OperationalRegisterMonthlyProjectionRow>, CancellationToken>(
                (_, _, rows, _) => written = rows)
            .Returns(Task.CompletedTask);

        await sut.RebuildMonthAsync(Context(registerId, period, movements.Object));

        written.Should().ContainSingle();
        written![0].DimensionSetId.Should().Be(positive);
        written[0].Values["movement_count"].Should().Be(1m);
        balances.Verify(x => x.ReplaceForMonthAsync(registerId, period, written, It.IsAny<CancellationToken>()), Times.Once);
        movements.Verify(x => x.GetByMonthAsync(registerId, period, null, 5, 2000, It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class ReadFixture
    {
        public Mock<IOperationalRegisterMovementsQueryReader> Movements { get; } = new(MockBehavior.Loose);
        public Mock<IOperationalRegisterTurnoversReader> Turnovers { get; } = new(MockBehavior.Loose);
        public Mock<IOperationalRegisterBalancesReader> Balances { get; } = new(MockBehavior.Loose);
        public OperationalRegisterReadService Sut { get; }

        public ReadFixture() => Sut = new(Movements.Object, Turnovers.Object, Balances.Object);
    }

    private static OperationalRegisterMovementQueryReadRow MovementRow(long id)
        => new() { MovementId = id, DocumentId = Guid.NewGuid(), OccurredAtUtc = Now,
            PeriodMonth = new DateOnly(2026, 1, 1), DimensionSetId = Guid.NewGuid() };

    private static OperationalRegisterMonthlyProjectionReadRow ProjectionRead(DateOnly month, Guid setId)
        => new() { PeriodMonth = month, DimensionSetId = setId };

    private static OperationalRegisterMonthlyProjectionRow Projection(
        Guid setId, params (string Key, decimal Value)[] values)
        => new(setId, values.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));

    private static OperationalRegisterMovementRead MovementRead(long id, Guid setId, bool storno)
        => new(id, Guid.NewGuid(), Now, setId, storno, new Dictionary<string, decimal>());

    private static OperationalRegisterMonthProjectionContext Context(
        Guid id, DateOnly month, IOperationalRegisterMovementsReader? movements = null)
        => new(id, "stock", "stock", month, Now,
            movements ?? new Mock<IOperationalRegisterMovementsReader>().Object);
}
