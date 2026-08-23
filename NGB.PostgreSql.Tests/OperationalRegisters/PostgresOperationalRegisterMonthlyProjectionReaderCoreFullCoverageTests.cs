using System.Data;
using FluentAssertions;
using Moq;
using NGB.Core.Dimensions;
using NGB.Core.Dimensions.Enrichment;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.PostgreSql.OperationalRegisters;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.OperationalRegisters;

public sealed class PostgresOperationalRegisterMonthlyProjectionReaderCoreFullCoverageTests
{
    private static readonly Guid RegisterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DimensionSetId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateOnly January = new(2026, 1, 1);

    [Fact]
    public async Task Reader_core_validates_register_range_limit_and_cursor_month()
    {
        Func<Task> emptyRegister = () => GetAsync(Guid.Empty, January, January);
        Func<Task> reversed = () => GetAsync(RegisterId, January.AddMonths(1), January);
        Func<Task> zeroLimit = () => GetPageAsync(RegisterId, January, January, null, 0);
        Func<Task> negativeLimit = () => GetPageAsync(RegisterId, January, January, null, -1);
        Func<Task> invalidCursorMonth = () => GetPageAsync(
            RegisterId, January, January, new DateOnly(2026, 1, 2), 1);

        await emptyRegister.Should().ThrowAsync<NgbArgumentRequiredException>();
        await reversed.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await zeroLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await negativeLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await invalidCursorMonth.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Reader_core_returns_empty_when_projection_table_is_absent()
    {
        var dimensionSets = new Mock<IDimensionSetReader>(MockBehavior.Strict);
        var enrichment = new Mock<IDimensionValueEnrichmentReader>(MockBehavior.Strict);
        var uow = new RecordingUnitOfWork(new RecordingDbConnection(scalar: _ => false));

        var rows = await PostgresOperationalRegisterMonthlyProjectionReaderCore.GetByMonthsAsync(
            uow,
            RegisterId,
            January,
            January,
            null,
            null,
            Resolve,
            dimensionSets.Object,
            enrichment.Object,
            default);

        rows.Should().BeEmpty();
        dimensionSets.VerifyNoOtherCalls();
        enrichment.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Reader_core_maps_missing_resource_to_zero_and_builds_paged_cursor_sql()
    {
        var dimensionId = Guid.NewGuid();
        var valueId = Guid.NewGuid();
        var bag = new DimensionBag([new DimensionValue(dimensionId, valueId)]);
        var dimensionSets = new Mock<IDimensionSetReader>(MockBehavior.Strict);
        dimensionSets.Setup(x => x.GetBagsByIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { DimensionSetId })),
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
        var connection = new RecordingDbConnection(
            readerFactory: _ => Rows(),
            scalar: _ => true);

        var rows = await PostgresOperationalRegisterMonthlyProjectionReaderCore.GetPageByMonthsAsync(
            new RecordingUnitOfWork(connection),
            RegisterId,
            January,
            January.AddMonths(1),
            null,
            null,
            January,
            DimensionSetId,
            1,
            Resolve,
            dimensionSets.Object,
            enrichment.Object,
            default);

        var row = rows.Should().ContainSingle().Subject;
        row.PeriodMonth.Should().Be(January);
        row.DimensionSetId.Should().Be(DimensionSetId);
        row.Values.Should().Contain("amount", 0m);
        row.Dimensions.Should().BeSameAs(bag);
        row.DimensionValueDisplays.Should().Contain(dimensionId, "Resolved");
        connection.Commands.Last().CommandText.Should().Contain("t.period_month > @AfterPeriodMonth").And.Contain("LIMIT @Limit");
        dimensionSets.VerifyAll();
        enrichment.VerifyAll();
    }

    [Fact]
    public async Task Reader_core_maps_empty_resource_sets_database_null_and_decimal_values()
    {
        foreach (var scenario in new[]
                 {
                     (Resources: (IReadOnlyList<string>)Array.Empty<string>(), IncludeAmount: false, Amount: (object?)null),
                     (Resources: (IReadOnlyList<string>)new[] { "amount" }, IncludeAmount: true, Amount: (object?)DBNull.Value),
                     (Resources: (IReadOnlyList<string>)new[] { "amount" }, IncludeAmount: true, Amount: (object?)8.5m)
                 })
        {
            var dimensionSets = new Mock<IDimensionSetReader>(MockBehavior.Strict);
            dimensionSets.Setup(x => x.GetBagsByIdsAsync(
                    It.IsAny<IReadOnlyCollection<Guid>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, DimensionBag>());
            var enrichment = new Mock<IDimensionValueEnrichmentReader>(MockBehavior.Strict);
            var connection = new RecordingDbConnection(
                readerFactory: _ => Rows(scenario.IncludeAmount, scenario.Amount),
                scalar: _ => true);

            var rows = await PostgresOperationalRegisterMonthlyProjectionReaderCore.GetByMonthsAsync(
                new RecordingUnitOfWork(connection),
                RegisterId,
                January,
                January,
                null,
                null,
                (_, _) => Task.FromResult<(string, IReadOnlyList<string>)>(
                    ("opreg_sales__projection", scenario.Resources)),
                dimensionSets.Object,
                enrichment.Object,
                default);

            var row = rows.Should().ContainSingle().Subject;
            if (scenario.Resources.Count == 0)
                row.Values.Should().BeEmpty();
            else
                row.Values.Should().Contain("amount", scenario.Amount is decimal value ? value : 0m);
        }
    }

    private static Task<IReadOnlyList<NGB.OperationalRegisters.Contracts.OperationalRegisterMonthlyProjectionReadRow>> GetAsync(
        Guid registerId,
        DateOnly from,
        DateOnly to)
        => PostgresOperationalRegisterMonthlyProjectionReaderCore.GetByMonthsAsync(
            null!, registerId, from, to, null, null, Resolve, null!, null!, default);

    private static Task<IReadOnlyList<NGB.OperationalRegisters.Contracts.OperationalRegisterMonthlyProjectionReadRow>> GetPageAsync(
        Guid registerId,
        DateOnly from,
        DateOnly to,
        DateOnly? cursor,
        int limit)
        => PostgresOperationalRegisterMonthlyProjectionReaderCore.GetPageByMonthsAsync(
            null!, registerId, from, to, null, null, cursor, null, limit, Resolve, null!, null!, default);

    private static Task<(string TableName, IReadOnlyList<string> ResourceColumns)> Resolve(
        Guid _,
        CancellationToken __)
        => Task.FromResult<(string, IReadOnlyList<string>)>(("opreg_sales__projection", ["amount"]));

    private static DataTableReader Rows(bool includeAmount = false, object? amount = null)
    {
        var table = new DataTable();
        table.Columns.Add("PeriodMonth", typeof(DateOnly));
        table.Columns.Add("DimensionSetId", typeof(Guid));
        if (includeAmount)
            table.Columns.Add("amount", typeof(decimal));
        if (includeAmount)
            table.Rows.Add(January, DimensionSetId, amount ?? DBNull.Value);
        else
            table.Rows.Add(January, DimensionSetId);
        return table.CreateDataReader();
    }
}
