using System.Data;
using FluentAssertions;
using NGB.OperationalRegisters.Contracts;
using NGB.PostgreSql.OperationalRegisters;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.OperationalRegisters;

public sealed class PostgresOperationalRegisterFinalizationRepositoryFullCoverageTests
{
    private static readonly Guid RegisterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly Period = new(2026, 8, 1);
    private static readonly DateTime NowUtc = new(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Get_returns_null_or_maps_complete_finalization()
    {
        (await Fixture().Repository.GetAsync(RegisterId, Period)).Should().BeNull();

        var expected = Finalization();
        var result = await Fixture(finalizations: [expected]).Repository.GetAsync(RegisterId, Period);
        result.Should().Be(expected);
    }

    [Fact]
    public async Task Mark_methods_validate_keys_utc_values_reason_boundaries_and_execute_each_upsert()
    {
        var sut = Fixture().Repository;
        Func<Task> finalizedEmptyId = () => sut.MarkFinalizedAsync(Guid.Empty, Period, NowUtc, NowUtc);
        Func<Task> finalizedBadPeriod = () => sut.MarkFinalizedAsync(RegisterId, Period.AddDays(1), NowUtc, NowUtc);
        Func<Task> finalizedLocalValue = () => sut.MarkFinalizedAsync(RegisterId, Period, NowUtc.ToLocalTime(), NowUtc);
        Func<Task> finalizedLocalNow = () => sut.MarkFinalizedAsync(RegisterId, Period, NowUtc, NowUtc.ToLocalTime());
        await finalizedEmptyId.Should().ThrowAsync<NgbArgumentInvalidException>();
        await finalizedBadPeriod.Should().ThrowAsync<NgbArgumentInvalidException>();
        await finalizedLocalValue.Should().ThrowAsync<NgbArgumentInvalidException>();
        await finalizedLocalNow.Should().ThrowAsync<NgbArgumentInvalidException>();

        Func<Task> dirtyLocalValue = () => sut.MarkDirtyAsync(RegisterId, Period, NowUtc.ToLocalTime(), NowUtc);
        Func<Task> dirtyLocalNow = () => sut.MarkDirtyAsync(RegisterId, Period, NowUtc, NowUtc.ToLocalTime());
        Func<Task> dirtyBatchNull = () => sut.MarkDirtyPeriodsAsync(RegisterId, null!, NowUtc, NowUtc);
        Func<Task> dirtyBatchEmptyId = () => sut.MarkDirtyPeriodsAsync(Guid.Empty, [Period], NowUtc, NowUtc);
        Func<Task> dirtyBatchBadPeriod = () => sut.MarkDirtyPeriodsAsync(RegisterId, [Period.AddDays(1)], NowUtc, NowUtc);
        await dirtyLocalValue.Should().ThrowAsync<NgbArgumentInvalidException>();
        await dirtyLocalNow.Should().ThrowAsync<NgbArgumentInvalidException>();
        await dirtyBatchNull.Should().ThrowAsync<ArgumentNullException>();
        await dirtyBatchEmptyId.Should().ThrowAsync<NgbArgumentInvalidException>();
        await dirtyBatchBadPeriod.Should().ThrowAsync<NgbArgumentInvalidException>();

        Func<Task> blockedLocalValue = () => sut.MarkBlockedNoProjectorAsync(
            RegisterId, Period, NowUtc.ToLocalTime(), "missing", NowUtc);
        Func<Task> blockedLocalNow = () => sut.MarkBlockedNoProjectorAsync(
            RegisterId, Period, NowUtc, "missing", NowUtc.ToLocalTime());
        Func<Task> blockedMissingReason = () => sut.MarkBlockedNoProjectorAsync(
            RegisterId, Period, NowUtc, " ", NowUtc);
        Func<Task> blockedLongReason = () => sut.MarkBlockedNoProjectorAsync(
            RegisterId, Period, NowUtc, new string('x', 129), NowUtc);
        await blockedLocalValue.Should().ThrowAsync<NgbArgumentInvalidException>();
        await blockedLocalNow.Should().ThrowAsync<NgbArgumentInvalidException>();
        await blockedMissingReason.Should().ThrowAsync<NgbArgumentRequiredException>();
        await blockedLongReason.Should().ThrowAsync<NgbArgumentOutOfRangeException>();

        var fixture = Fixture();
        await fixture.Repository.MarkFinalizedAsync(RegisterId, Period, NowUtc, NowUtc);
        await fixture.Repository.MarkDirtyPeriodsAsync(
            RegisterId,
            [Period.AddMonths(1), Period, Period.AddMonths(1)],
            NowUtc,
            NowUtc);
        await fixture.Repository.MarkDirtyPeriodsAsync(RegisterId, [], NowUtc, NowUtc);
        await fixture.Repository.MarkBlockedNoProjectorAsync(
            RegisterId, Period, NowUtc, new string('x', 128), NowUtc);
        fixture.Connection.Commands.Should().HaveCount(3);
        fixture.Connection.Commands[0].CommandText.Should().Contain("finalized_at_utc = EXCLUDED.finalized_at_utc");
        fixture.Connection.Commands[1].CommandText.Should()
            .Contain("FROM UNNEST(")
            .And.Contain("::date[]")
            .And.Contain("dirty_since_utc = EXCLUDED.dirty_since_utc");
        fixture.Connection.Commands[2].CommandText.Should().Contain("blocked_reason = EXCLUDED.blocked_reason");
    }

    [Fact]
    public async Task Filtered_list_methods_validate_ids_and_limits_and_map_rows()
    {
        var sut = Fixture().Repository;
        Func<Task> dirtyEmptyId = () => sut.GetDirtyAsync(Guid.Empty);
        Func<Task> dirtyLimit = () => sut.GetDirtyAsync(RegisterId, 0);
        Func<Task> blockedEmptyId = () => sut.GetBlockedAsync(Guid.Empty);
        Func<Task> blockedLimit = () => sut.GetBlockedAsync(RegisterId, -1);
        Func<Task> dirtyAllLimit = () => sut.GetDirtyAcrossAllAsync(0);
        Func<Task> blockedAllLimit = () => sut.GetBlockedAcrossAllAsync(0);
        Func<Task> dirtyHighLimit = () => sut.GetDirtyAsync(
            RegisterId,
            OperationalRegisterFinalizationLimits.MaxReadPageSize + 1);
        Func<Task> blockedHighLimit = () => sut.GetBlockedAsync(
            RegisterId,
            OperationalRegisterFinalizationLimits.MaxReadPageSize + 1);
        Func<Task> dirtyAllHighLimit = () => sut.GetDirtyAcrossAllAsync(
            OperationalRegisterFinalizationLimits.MaxReadPageSize + 1);
        Func<Task> blockedAllHighLimit = () => sut.GetBlockedAcrossAllAsync(
            OperationalRegisterFinalizationLimits.MaxReadPageSize + 1);
        await dirtyEmptyId.Should().ThrowAsync<NgbArgumentInvalidException>();
        await dirtyLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await blockedEmptyId.Should().ThrowAsync<NgbArgumentInvalidException>();
        await blockedLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await dirtyAllLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await blockedAllLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await dirtyHighLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await blockedHighLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await dirtyAllHighLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await blockedAllHighLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();

        var expected = Finalization();
        var fixture = Fixture(finalizations: [expected]);
        (await fixture.Repository.GetDirtyAsync(RegisterId, 1)).Should().Equal(expected);
        (await fixture.Repository.GetBlockedAsync(RegisterId, 1)).Should().Equal(expected);
        (await fixture.Repository.GetDirtyAcrossAllAsync(1)).Should().Equal(expected);
        (await fixture.Repository.GetBlockedAcrossAllAsync(1)).Should().Equal(expected);
        fixture.Connection.Commands.Should().Contain(command => command.CommandText.Contains("ORDER BY blocked_since_utc, register_id, period"));
    }

    [Fact]
    public async Task Period_queries_validate_month_boundaries_and_return_tracked_and_latest_values()
    {
        var sut = Fixture().Repository;
        Func<Task> trackedEmptyId = () => sut.GetTrackedPeriodsOnOrAfterAsync(Guid.Empty, Period);
        Func<Task> trackedBadPeriod = () => sut.GetTrackedPeriodsOnOrAfterAsync(RegisterId, Period.AddDays(1));
        Func<Task> latestEmptyId = () => sut.GetLatestFinalizedPeriodBeforeAsync(Guid.Empty, Period);
        Func<Task> latestBadPeriod = () => sut.GetLatestFinalizedPeriodBeforeAsync(RegisterId, Period.AddDays(1));
        await trackedEmptyId.Should().ThrowAsync<NgbArgumentInvalidException>();
        await trackedBadPeriod.Should().ThrowAsync<NgbArgumentInvalidException>();
        await latestEmptyId.Should().ThrowAsync<NgbArgumentInvalidException>();
        await latestBadPeriod.Should().ThrowAsync<NgbArgumentInvalidException>();

        var fixture = Fixture(periods: [Period, Period.AddMonths(1)]);
        (await fixture.Repository.GetTrackedPeriodsOnOrAfterAsync(RegisterId, Period))
            .Should().Equal(Period, Period.AddMonths(1));
        (await fixture.Repository.GetLatestFinalizedPeriodBeforeAsync(RegisterId, Period.AddMonths(2)))
            .Should().Be(Period);

        (await Fixture().Repository.GetLatestFinalizedPeriodBeforeAsync(RegisterId, Period.AddMonths(2)))
            .Should().BeNull();
    }

    private static RepositoryFixture Fixture(
        IReadOnlyList<OperationalRegisterFinalization>? finalizations = null,
        IReadOnlyList<DateOnly>? periods = null)
        => new(finalizations ?? [], periods ?? []);

    private static OperationalRegisterFinalization Finalization()
        => new(
            RegisterId,
            Period,
            OperationalRegisterFinalizationStatus.BlockedNoProjector,
            NowUtc.AddHours(-3),
            NowUtc.AddHours(-2),
            NowUtc.AddHours(-1),
            "no projector",
            NowUtc.AddDays(-1),
            NowUtc);

    private sealed class RepositoryFixture(
        IReadOnlyList<OperationalRegisterFinalization> finalizations,
        IReadOnlyList<DateOnly> periods)
    {
        public RecordingDbConnection Connection { get; } = new(
            readerFactory: sql => sql.TrimStart().StartsWith("SELECT period", StringComparison.Ordinal)
                ? PeriodRows(sql.Contains("LIMIT 1", StringComparison.Ordinal) ? periods.Take(1).ToArray() : periods)
                : FinalizationRows(finalizations));

        public PostgresOperationalRegisterFinalizationRepository Repository => new(
            new RecordingUnitOfWork(Connection, hasActiveTransaction: true));
    }

    private static System.Data.Common.DbDataReader FinalizationRows(
        IReadOnlyList<OperationalRegisterFinalization> rows)
    {
        var table = new DataTable();
        table.Columns.Add("RegisterId", typeof(Guid));
        table.Columns.Add("Period", typeof(DateOnly));
        table.Columns.Add("Status", typeof(short));
        table.Columns.Add("FinalizedAtUtc", typeof(DateTime));
        table.Columns.Add("DirtySinceUtc", typeof(DateTime));
        table.Columns.Add("BlockedSinceUtc", typeof(DateTime));
        table.Columns.Add("BlockedReason", typeof(string));
        table.Columns.Add("CreatedAtUtc", typeof(DateTime));
        table.Columns.Add("UpdatedAtUtc", typeof(DateTime));
        foreach (var row in rows)
        {
            table.Rows.Add(
                row.RegisterId,
                row.Period,
                (short)row.Status,
                row.FinalizedAtUtc ?? (object)DBNull.Value,
                row.DirtySinceUtc ?? (object)DBNull.Value,
                row.BlockedSinceUtc ?? (object)DBNull.Value,
                row.BlockedReason ?? (object)DBNull.Value,
                row.CreatedAtUtc,
                row.UpdatedAtUtc);
        }
        return table.CreateDataReader();
    }

    private static System.Data.Common.DbDataReader PeriodRows(IReadOnlyList<DateOnly> periods)
    {
        var table = new DataTable();
        table.Columns.Add("period", typeof(DateOnly));
        foreach (var period in periods) table.Rows.Add(period);
        return table.CreateDataReader();
    }
}
