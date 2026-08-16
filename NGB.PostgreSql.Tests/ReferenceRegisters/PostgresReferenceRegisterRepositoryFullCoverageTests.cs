using System.Data;
using FluentAssertions;
using NGB.PostgreSql.ReferenceRegisters;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.PostgreSql.Tests.ReferenceRegisters;

public sealed class PostgresReferenceRegisterRepositoryFullCoverageTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Utc = new(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Reads_validate_empty_inputs_normalize_parameters_and_map_empty_or_present_rows()
    {
        var connection = new RecordingDbConnection(readerFactory: _ => RegisterRows(true));
        var sut = Repository(connection);
        (await sut.GetAllAsync(default)).Should().ContainSingle();
        (await sut.GetByIdAsync(Id, default)).Should().NotBeNull();
        (await sut.GetByIdsAsync([Id, Id], default)).Should().ContainSingle();
        (await sut.GetByCodeAsync("  PRICES ", default)).Should().NotBeNull();
        (await sut.GetByTableCodeAsync("  prices  ", default)).Should().NotBeNull();

        Func<Task> nullIds = async () => await sut.GetByIdsAsync(null!, default);
        Func<Task> emptyCode = async () => await sut.GetByCodeAsync(" ", default);
        Func<Task> emptyTable = async () => await sut.GetByTableCodeAsync("", default);
        await nullIds.Should().ThrowAsync<NgbArgumentRequiredException>();
        await emptyCode.Should().ThrowAsync<NgbArgumentRequiredException>();
        await emptyTable.Should().ThrowAsync<NgbArgumentRequiredException>();
        (await sut.GetByIdsAsync([], default)).Should().BeEmpty();

        var empty = Repository(new RecordingDbConnection(readerFactory: _ => RegisterRows(false)));
        (await empty.GetByIdAsync(Id, default)).Should().BeNull();
        (await empty.GetByCodeAsync("prices", default)).Should().BeNull();
        (await empty.GetByTableCodeAsync("prices", default)).Should().BeNull();
    }

    [Fact]
    public async Task Upsert_validates_payload_trims_values_and_executes_in_transaction()
    {
        var sut = Repository(new RecordingDbConnection());
        Func<Task> nullPayload = () => sut.UpsertAsync(null!, Utc, default);
        Func<Task> local = () => sut.UpsertAsync(Upsert(), DateTime.SpecifyKind(Utc, DateTimeKind.Local), default);
        Func<Task> emptyId = () => sut.UpsertAsync(Upsert() with { RegisterId = Guid.Empty }, Utc, default);
        Func<Task> emptyCode = () => sut.UpsertAsync(Upsert() with { Code = " " }, Utc, default);
        Func<Task> emptyName = () => sut.UpsertAsync(Upsert() with { Name = "" }, Utc, default);
        await nullPayload.Should().ThrowAsync<NgbArgumentRequiredException>();
        await local.Should().ThrowAsync<NgbArgumentInvalidException>();
        await emptyId.Should().ThrowAsync<NgbArgumentRequiredException>();
        await emptyCode.Should().ThrowAsync<NgbArgumentRequiredException>();
        await emptyName.Should().ThrowAsync<NgbArgumentRequiredException>();

        var connection = new RecordingDbConnection();
        await Repository(connection).UpsertAsync(Upsert(), Utc, default);
        connection.Commands.Should().ContainSingle();
        connection.Commands[0].CommandText.Should().Contain("ON CONFLICT");
        connection.Commands[0].ParametersSnapshot.Single(x => x.ParameterName == "Code").Value.Should().Be("Prices");
    }

    [Theory]
    [InlineData("ux_reference_registers_table_code")]
    [InlineData("reference_registers_table_code_key")]
    public async Task Upsert_translates_table_code_unique_collision(string constraint)
    {
        var exception = Pg(PostgresErrorCodes.UniqueViolation, constraint);
        var connection = new RecordingDbConnection(
            readerFactory: _ => CollisionRows(true),
            nonQuery: _ => throw exception);

        Func<Task> act = () => Repository(connection).UpsertAsync(Upsert(), Utc, default);
        await act.Should().ThrowAsync<ReferenceRegisterTableCodeCollisionException>();
        connection.Commands.Last().CommandText.Should().Contain("WHERE table_code = @TableCode");
    }

    [Fact]
    public async Task Upsert_handles_missing_collision_row_and_does_not_translate_other_postgres_errors()
    {
        var unique = Pg(PostgresErrorCodes.UniqueViolation, "ux_reference_registers_table_code");
        var missing = new RecordingDbConnection(readerFactory: _ => CollisionRows(false), nonQuery: _ => throw unique);
        Func<Task> missingRow = () => Repository(missing).UpsertAsync(Upsert(), Utc, default);
        await missingRow.Should().ThrowAsync<NgbInvariantViolationException>();

        var other = Pg("42601", "ux_reference_registers_table_code");
        var syntax = new RecordingDbConnection(nonQuery: _ => throw other);
        Func<Task> otherError = () => Repository(syntax).UpsertAsync(Upsert(), Utc, default);
        var thrown = await otherError.Should().ThrowAsync<PostgresException>();
        thrown.Which.Should().BeSameAs(other);
    }

    private static PostgresReferenceRegisterRepository Repository(RecordingDbConnection connection)
        => new(new RecordingUnitOfWork(connection, hasActiveTransaction: true));

    private static ReferenceRegisterUpsert Upsert()
        => new(Id, "  Prices  ", "  Price register  ", ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.Independent);

    private static PostgresException Pg(string state, string constraint)
        => new("error", "ERROR", "ERROR", state, "", "", 0, 0, "", "", "public", "reference_registers",
            "table_code", "text", constraint, "file", "1", "routine");

    private static System.Data.Common.DbDataReader RegisterRows(bool include)
    {
        var t = new DataTable();
        t.Columns.Add("RegisterId", typeof(Guid)); t.Columns.Add("Code", typeof(string));
        t.Columns.Add("CodeNorm", typeof(string)); t.Columns.Add("TableCode", typeof(string));
        t.Columns.Add("Name", typeof(string)); t.Columns.Add("Periodicity", typeof(short));
        t.Columns.Add("RecordMode", typeof(short)); t.Columns.Add("HasRecords", typeof(bool));
        t.Columns.Add("CreatedAtUtc", typeof(DateTime)); t.Columns.Add("UpdatedAtUtc", typeof(DateTime));
        if (include) t.Rows.Add(Id, "Prices", "prices", "prices", "Prices", (short)0, (short)0, false, Utc, Utc);
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
