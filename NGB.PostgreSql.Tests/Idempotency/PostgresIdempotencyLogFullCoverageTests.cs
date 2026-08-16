using System.Data;
using FluentAssertions;
using NGB.Accounting.PostingState;
using NGB.PostgreSql.Idempotency;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.Idempotency;

public sealed class PostgresIdempotencyLogFullCoverageTests
{
    private static readonly DateTime Now = new(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly PostgresIdempotencyLog.Key[] Keys =
    [
        new("entity_id", "EntityId", Guid.Parse("11111111-1111-1111-1111-111111111111"))
    ];

    [Fact]
    public async Task Try_begin_validates_transaction_time_table_and_keys()
    {
        var inactive = new RecordingUnitOfWork(new RecordingDbConnection(nonQuery: _ => 1));
        Func<Task> noTransaction = () => TryBegin(inactive);
        await noTransaction.Should().ThrowAsync<InvalidOperationException>();

        var active = UnitOfWork(nonQueries: [1]);
        Func<Task> localTime = () => PostgresIdempotencyLog.TryBeginAsync(
            active, "state", null, Keys, DateTime.SpecifyKind(Now, DateTimeKind.Local), null, () => "missing", default);
        Func<Task> blankTable = () => PostgresIdempotencyLog.TryBeginAsync(
            UnitOfWork(nonQueries: [1]), " ", null, Keys, Now, null, () => "missing", default);
        Func<Task> nullKeys = () => PostgresIdempotencyLog.TryBeginAsync(
            UnitOfWork(nonQueries: [1]), "state", null, null!, Now, null, () => "missing", default);
        Func<Task> emptyKeys = () => PostgresIdempotencyLog.TryBeginAsync(
            UnitOfWork(nonQueries: [1]), "state", null, [], Now, null, () => "missing", default);

        await localTime.Should().ThrowAsync<NgbArgumentInvalidException>();
        await blankTable.Should().ThrowAsync<NgbArgumentRequiredException>();
        await nullKeys.Should().ThrowAsync<NgbArgumentRequiredException>();
        await emptyKeys.Should().ThrowAsync<NgbArgumentRequiredException>();
    }

    [Fact]
    public async Task Try_begin_returns_begun_for_insert_with_or_without_history()
    {
        var withHistory = UnitOfWork(nonQueries: [1, 1]);
        var withoutHistory = UnitOfWork(nonQueries: [1]);
        var whitespaceHistory = UnitOfWork(nonQueries: [1]);

        (await TryBegin(withHistory, "history")).Should().Be(PostingStateBeginResult.Begun);
        (await TryBegin(withoutHistory)).Should().Be(PostingStateBeginResult.Begun);
        (await TryBegin(whitespaceHistory, " ")).Should().Be(PostingStateBeginResult.Begun);

        ((RecordingDbConnection)withHistory.Connection).Commands.Should().HaveCount(2);
        ((RecordingDbConnection)withHistory.Connection).Commands[1].CommandText.Should()
            .Contain("INSERT INTO history").And.Contain("event_kind");
    }

    [Fact]
    public async Task Try_begin_handles_missing_completed_and_current_rows()
    {
        var missing = UnitOfWork(nonQueries: [0], tables: [LogRows()]);
        Func<Task> missingAct = () => TryBegin(missing);
        await missingAct.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("missing state row");

        var completed = UnitOfWork(
            nonQueries: [0],
            tables: [LogRows((Guid.NewGuid(), Now.AddMinutes(-1), Now))]);
        (await TryBegin(completed)).Should().Be(PostingStateBeginResult.AlreadyCompleted);

        var current = UnitOfWork(
            nonQueries: [0],
            tables: [LogRows((Guid.NewGuid(), Now.AddMinutes(-1), null))]);
        (await TryBegin(current)).Should().Be(PostingStateBeginResult.InProgress);
    }

    [Fact]
    public async Task Try_begin_takes_over_stale_rows_and_records_superseded_only_for_known_attempts()
    {
        var oldAttempt = Guid.NewGuid();
        var known = UnitOfWork(
            nonQueries: [0, 1, 1, 1],
            tables: [LogRows((oldAttempt, Now.AddHours(-1), null))]);
        var unknown = UnitOfWork(
            nonQueries: [0, 1, 1],
            tables: [LogRows((null, Now.AddHours(-1), null))]);

        (await TryBegin(known, "history", TimeSpan.FromMinutes(5))).Should().Be(PostingStateBeginResult.Begun);
        (await TryBegin(unknown, "history", TimeSpan.FromMinutes(5))).Should().Be(PostingStateBeginResult.Begun);

        var knownCommands = ((RecordingDbConnection)known.Connection).Commands;
        knownCommands.Should().HaveCount(5);
        knownCommands[2].CommandText.Should().Contain("UPDATE state");
        knownCommands[3].ParametersSnapshot.Should().Contain(x =>
            x.ParameterName == "EventKind" && Equals(x.Value, (short)3));
        knownCommands[4].ParametersSnapshot.Should().Contain(x =>
            x.ParameterName == "EventKind" && Equals(x.Value, (short)1));
        ((RecordingDbConnection)unknown.Connection).Commands.Should().HaveCount(4);
    }

    [Fact]
    public async Task Try_begin_handles_takeover_races_with_completed_missing_and_in_progress_rereads()
    {
        var stale = (AttemptId: (Guid?)Guid.NewGuid(), StartedAt: Now.AddHours(-1), CompletedAt: (DateTime?)null);
        var completedRace = UnitOfWork(
            nonQueries: [0, 0],
            tables:
            [
                LogRows(stale),
                LogRows((stale.AttemptId, stale.StartedAt, Now))
            ]);
        var missingRace = UnitOfWork(
            nonQueries: [0, 0],
            tables: [LogRows(stale), LogRows()]);
        var activeRace = UnitOfWork(
            nonQueries: [0, 0],
            tables:
            [
                LogRows(stale),
                LogRows((stale.AttemptId, Now, null))
            ]);

        (await TryBegin(completedRace, timeout: TimeSpan.FromMinutes(5)))
            .Should().Be(PostingStateBeginResult.AlreadyCompleted);
        (await TryBegin(missingRace, timeout: TimeSpan.FromMinutes(5)))
            .Should().Be(PostingStateBeginResult.InProgress);
        (await TryBegin(activeRace, timeout: TimeSpan.FromMinutes(5)))
            .Should().Be(PostingStateBeginResult.InProgress);
    }

    [Fact]
    public async Task Mark_completed_validates_inputs_and_handles_zero_one_and_multiple_rows()
    {
        Func<Task> localTime = () => MarkCompleted(
            UnitOfWork(tables: [CompletedRows()]), DateTime.SpecifyKind(Now, DateTimeKind.Local));
        Func<Task> blankTable = () => MarkCompleted(UnitOfWork(tables: [CompletedRows()]), Now, table: " ");
        Func<Task> nullKeys = () => PostgresIdempotencyLog.MarkCompletedAsync(
            UnitOfWork(tables: [CompletedRows()]), "state", null, null!, Now,
            () => "multiple rows", null, default);
        Func<Task> emptyKeys = () => MarkCompleted(UnitOfWork(tables: [CompletedRows()]), Now, keys: []);
        await localTime.Should().ThrowAsync<NgbArgumentInvalidException>();
        await blankTable.Should().ThrowAsync<NgbArgumentRequiredException>();
        await nullKeys.Should().ThrowAsync<NgbArgumentRequiredException>();
        await emptyKeys.Should().ThrowAsync<NgbArgumentRequiredException>();

        var noRows = UnitOfWork(tables: [CompletedRows()]);
        await MarkCompleted(noRows, Now);
        ((RecordingDbConnection)noRows.Connection).Commands.Should().ContainSingle();

        var attempt = Guid.NewGuid();
        var completed = UnitOfWork(
            nonQueries: [1],
            tables: [CompletedRows((attempt, Now))]);
        await MarkCompleted(completed, Now, historyTable: "history");
        ((RecordingDbConnection)completed.Connection).Commands.Should().HaveCount(2);

        var legacy = UnitOfWork(tables: [CompletedRows((null, Now))]);
        await MarkCompleted(legacy, Now, historyTable: "history");
        ((RecordingDbConnection)legacy.Connection).Commands.Should().ContainSingle();

        var manyWithContext = UnitOfWork(tables: [CompletedRows((attempt, Now), (Guid.NewGuid(), Now))]);
        Func<Task> manyAct = () => MarkCompleted(
            manyWithContext,
            Now,
            context: new Dictionary<string, object?> { ["entity"] = "one", ["rows"] = "preserved" });
        var withContext = await manyAct.Should().ThrowAsync<NgbInvariantViolationException>();
        withContext.Which.Context.Should().Contain("entity", "one").And.Contain("rows", "preserved");

        var manyWithoutContext = UnitOfWork(tables: [CompletedRows((attempt, Now), (Guid.NewGuid(), Now))]);
        Func<Task> manyNoContext = () => MarkCompleted(manyWithoutContext, Now, context: null);
        var withoutContext = await manyNoContext.Should().ThrowAsync<NgbInvariantViolationException>();
        withoutContext.Which.Context.Should().Contain("rows", 2);
    }

    private static Task<PostingStateBeginResult> TryBegin(
        RecordingUnitOfWork uow,
        string? historyTable = null,
        TimeSpan? timeout = null)
        => PostgresIdempotencyLog.TryBeginAsync(
            uow,
            "state",
            historyTable,
            Keys,
            Now,
            timeout,
            () => "missing state row",
            default);

    private static Task MarkCompleted(
        RecordingUnitOfWork uow,
        DateTime completedAt,
        string table = "state",
        string? historyTable = null,
        IReadOnlyList<PostgresIdempotencyLog.Key>? keys = null,
        IDictionary<string, object?>? context = null)
        => PostgresIdempotencyLog.MarkCompletedAsync(
            uow,
            table,
            historyTable,
            keys ?? Keys,
            completedAt,
            () => "multiple rows",
            context,
            default);

    private static RecordingUnitOfWork UnitOfWork(
        IEnumerable<int>? nonQueries = null,
        IEnumerable<DataTable>? tables = null)
    {
        var results = new Queue<int>(nonQueries ?? []);
        var readers = new Queue<DataTable>(tables ?? []);
        var connection = new RecordingDbConnection(
            readerFactory: _ => readers.Dequeue().CreateDataReader(),
            nonQuery: _ => results.Count == 0 ? 1 : results.Dequeue());
        return new RecordingUnitOfWork(connection, hasActiveTransaction: true);
    }

    private static DataTable LogRows(
        params (Guid? AttemptId, DateTime StartedAt, DateTime? CompletedAt)[] rows)
    {
        var table = new DataTable();
        table.Columns.Add("AttemptId", typeof(Guid));
        table.Columns.Add("StartedAtUtc", typeof(DateTime));
        table.Columns.Add("CompletedAtUtc", typeof(DateTime));
        foreach (var row in rows)
            table.Rows.Add(row.AttemptId ?? (object)DBNull.Value, row.StartedAt, row.CompletedAt ?? (object)DBNull.Value);
        return table;
    }

    private static DataTable CompletedRows(params (Guid? AttemptId, DateTime CompletedAt)[] rows)
    {
        var table = new DataTable();
        table.Columns.Add("AttemptId", typeof(Guid));
        table.Columns.Add("CompletedAtUtc", typeof(DateTime));
        foreach (var row in rows)
            table.Rows.Add(row.AttemptId ?? (object)DBNull.Value, row.CompletedAt);
        return table;
    }
}
