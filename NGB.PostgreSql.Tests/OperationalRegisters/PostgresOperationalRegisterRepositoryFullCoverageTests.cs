using System.Data;
using FluentAssertions;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.PostgreSql.OperationalRegisters;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.PostgreSql.Tests.OperationalRegisters;

public sealed class PostgresOperationalRegisterRepositoryFullCoverageTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Utc = new(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Reads_validate_normalize_short_circuit_and_map_present_or_missing_rows()
    {
        var connection = new RecordingDbConnection(readerFactory: _ => Rows(true));
        var sut = Repository(connection);
        (await sut.GetAllAsync(default)).Should().ContainSingle();
        (await sut.GetByIdAsync(Id, default)).Should().NotBeNull();
        (await sut.GetByIdsAsync([Id, Id], default)).Should().ContainSingle();
        (await sut.GetByCodeAsync("  TURNOVER  ", default)).Should().NotBeNull();
        (await sut.GetByTableCodeAsync("  turnover  ", default)).Should().NotBeNull();
        Func<Task> nullIds = async () => await sut.GetByIdsAsync(null!, default);
        Func<Task> emptyCode = async () => await sut.GetByCodeAsync(" ", default);
        Func<Task> emptyTable = async () => await sut.GetByTableCodeAsync("", default);
        await nullIds.Should().ThrowAsync<NgbArgumentRequiredException>();
        await emptyCode.Should().ThrowAsync<NgbArgumentRequiredException>();
        await emptyTable.Should().ThrowAsync<NgbArgumentRequiredException>();
        (await sut.GetByIdsAsync([], default)).Should().BeEmpty();

        var empty = Repository(new RecordingDbConnection(readerFactory: _ => Rows(false)));
        (await empty.GetByIdAsync(Id, default)).Should().BeNull();
        (await empty.GetByCodeAsync("turnover", default)).Should().BeNull();
        (await empty.GetByTableCodeAsync("turnover", default)).Should().BeNull();
    }

    [Fact]
    public async Task Upsert_validates_trims_and_executes()
    {
        var sut = Repository(new RecordingDbConnection());
        Func<Task> nullValue = () => sut.UpsertAsync(null!, Utc, default);
        Func<Task> local = () => sut.UpsertAsync(Value(), DateTime.SpecifyKind(Utc, DateTimeKind.Local), default);
        Func<Task> emptyId = () => sut.UpsertAsync(Value() with { RegisterId = Guid.Empty }, Utc, default);
        Func<Task> emptyCode = () => sut.UpsertAsync(Value() with { Code = " " }, Utc, default);
        Func<Task> emptyName = () => sut.UpsertAsync(Value() with { Name = "" }, Utc, default);
        await nullValue.Should().ThrowAsync<NgbArgumentRequiredException>();
        await local.Should().ThrowAsync<NgbArgumentInvalidException>();
        await emptyId.Should().ThrowAsync<NgbArgumentRequiredException>();
        await emptyCode.Should().ThrowAsync<NgbArgumentRequiredException>();
        await emptyName.Should().ThrowAsync<NgbArgumentRequiredException>();

        var connection = new RecordingDbConnection();
        await Repository(connection).UpsertAsync(Value(), Utc, default);
        connection.Commands.Should().ContainSingle();
        connection.Commands[0].ParametersSnapshot.Single(x => x.ParameterName == "Code").Value.Should().Be("Turnover");
    }

    [Theory]
    [InlineData("ux_operational_registers_table_code")]
    [InlineData("operational_registers_table_code_key")]
    public async Task Upsert_translates_table_code_collisions(string constraint)
    {
        var pg = Pg(PostgresErrorCodes.UniqueViolation, constraint);
        var connection = new RecordingDbConnection(readerFactory: _ => CollisionRows(true), nonQuery: _ => throw pg);
        Func<Task> act = () => Repository(connection).UpsertAsync(Value(), Utc, default);
        await act.Should().ThrowAsync<OperationalRegisterTableCodeCollisionException>();
        connection.Commands.Last().CommandText.Should().Contain("WHERE table_code = @TableCode");
    }

    [Fact]
    public async Task Upsert_handles_missing_collision_row_and_propagates_unrelated_errors()
    {
        var unique = Pg(PostgresErrorCodes.UniqueViolation, "ux_operational_registers_table_code");
        var missing = new RecordingDbConnection(readerFactory: _ => CollisionRows(false), nonQuery: _ => throw unique);
        Func<Task> missingRow = () => Repository(missing).UpsertAsync(Value(), Utc, default);
        await missingRow.Should().ThrowAsync<NgbInvariantViolationException>();

        var other = Pg("42601", "ux_operational_registers_table_code");
        Func<Task> unrelated = () => Repository(new RecordingDbConnection(nonQuery: _ => throw other))
            .UpsertAsync(Value(), Utc, default);
        (await unrelated.Should().ThrowAsync<PostgresException>()).Which.Should().BeSameAs(other);
    }

    private static PostgresOperationalRegisterRepository Repository(RecordingDbConnection connection)
        => new(new RecordingUnitOfWork(connection, hasActiveTransaction: true));

    private static OperationalRegisterUpsert Value() => new(Id, "  Turnover  ", "  Turnover register  ");

    private static PostgresException Pg(string state, string constraint)
        => new("error", "ERROR", "ERROR", state, "", "", 0, 0, "", "", "public", "operational_registers",
            "table_code", "text", constraint, "file", "1", "routine");

    private static System.Data.Common.DbDataReader Rows(bool include)
    {
        var t = new DataTable();
        t.Columns.Add("RegisterId", typeof(Guid)); t.Columns.Add("Code", typeof(string));
        t.Columns.Add("CodeNorm", typeof(string)); t.Columns.Add("TableCode", typeof(string));
        t.Columns.Add("Name", typeof(string)); t.Columns.Add("HasMovements", typeof(bool));
        t.Columns.Add("CreatedAtUtc", typeof(DateTime)); t.Columns.Add("UpdatedAtUtc", typeof(DateTime));
        if (include) t.Rows.Add(Id, "Turnover", "turnover", "turnover", "Turnover", false, Utc, Utc);
        return t.CreateDataReader();
    }

    private static System.Data.Common.DbDataReader CollisionRows(bool include)
    {
        var t = new DataTable();
        t.Columns.Add("RegisterId", typeof(Guid)); t.Columns.Add("Code", typeof(string)); t.Columns.Add("CodeNorm", typeof(string));
        if (include) t.Rows.Add(Guid.NewGuid(), "Other", "other");
        return t.CreateDataReader();
    }
}
