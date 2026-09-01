using FluentAssertions;
using NGB.Contracts.Reporting;
using NGB.PostgreSql.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.Reporting;

public sealed class PostgresReportCursorPagingFullCoverageTests
{
    [Fact]
    public void Detail_query_uses_hidden_stable_key_and_cursor_page_uses_seek_predicate()
    {
        var sut = Builder(CursorDataset());
        var first = sut.Build(Request());

        first.Sql.Should().Contain("r.id AS __cursor_key_0")
            .And.Contain("ORDER BY name_out ASC NULLS LAST, __cursor_key_0 ASC NULLS LAST")
            .And.NotContain("OFFSET @offset");
        first.CursorColumns.Should().HaveCount(2);
        first.CursorColumns[^1].IsHidden.Should().BeTrue();

        var cursor = PostgresReportCursorCodec.Encode(
            first.DatasetCode,
            first.CursorColumns,
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["name_out"] = "Alpha",
                ["__cursor_key_0"] = 42L
            });
        var next = sut.Build(Request(cursor, offset: 999_999));

        next.Offset.Should().Be(0);
        next.Sql.Should().Contain("FROM (\n")
            .And.Contain("name_out IS NULL OR name_out > @cursor_0")
            .And.Contain("name_out IS NOT DISTINCT FROM @cursor_0")
            .And.Contain("__cursor_key_0 IS NULL OR __cursor_key_0 > @cursor_1")
            .And.NotContain("OFFSET @offset");
        next.Parameters.ParameterNames.Should().Contain(["cursor_0", "cursor_1", "limit_plus_one"]);
    }

    [Fact]
    public void Cursor_codec_round_trips_supported_values_and_rejects_invalid_tokens_or_rows()
    {
        var columns = new[]
        {
            Column("null"), Column("string"), Column("guid"), Column("datetime"),
            Column("datetimeoffset"), Column("date"), Column("bool"), Column("byte"),
            Column("int"), Column("uint"), Column("long"), Column("decimal"),
            Column("float"), Column("double")
        };
        var guid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var utc = new DateTime(2026, 8, 30, 12, 34, 56, DateTimeKind.Utc);
        var dto = new DateTimeOffset(2026, 8, 30, 12, 34, 56, TimeSpan.FromHours(2));
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["null"] = null,
            ["string"] = "text",
            ["guid"] = guid,
            ["datetime"] = utc,
            ["datetimeoffset"] = dto,
            ["date"] = new DateOnly(2026, 8, 30),
            ["bool"] = true,
            ["byte"] = (byte)7,
            ["int"] = 8,
            ["uint"] = (uint)9,
            ["long"] = 10L,
            ["decimal"] = 11.25m,
            ["float"] = 12.5f,
            ["double"] = 13.75d
        };

        var token = PostgresReportCursorCodec.Encode("dataset", columns, row);
        var values = PostgresReportCursorCodec.Decode(token, "DATASET", columns);

        values.Should().Equal(new object?[]
        {
            null, "text", guid, utc, dto, new DateOnly(2026, 8, 30), true,
            7L, 8L, 9L, 10L, 11.25m, 12.5d, 13.75d
        });

        Action blank = () => PostgresReportCursorCodec.Decode(" ", "dataset", columns);
        Action malformed = () => PostgresReportCursorCodec.Decode("not-base64!", "dataset", columns);
        Action wrongDataset = () => PostgresReportCursorCodec.Decode(token, "other", columns);
        Action wrongShape = () => PostgresReportCursorCodec.Decode(token, "dataset", [Column("other")]);
        Action missingValue = () => PostgresReportCursorCodec.Encode("dataset", columns, new Dictionary<string, object?>());
        Action unsupportedValue = () => PostgresReportCursorCodec.Encode(
            "dataset",
            [Column("value")],
            new Dictionary<string, object?> { ["value"] = new object() });

        blank.Should().Throw<NgbArgumentInvalidException>();
        malformed.Should().Throw<NgbArgumentInvalidException>();
        wrongDataset.Should().Throw<NgbArgumentInvalidException>();
        wrongShape.Should().Throw<NgbArgumentInvalidException>();
        missingValue.Should().Throw<NgbInvariantViolationException>();
        unsupportedValue.Should().Throw<NgbInvariantViolationException>();
    }

    [Fact]
    public void Cursor_is_rejected_for_unaggregated_dataset_without_stable_keys()
    {
        var dataset = new PostgresReportDatasetBinding(
            "dataset",
            "rows r",
            [new("name", "r.name", "string")],
            []);
        var sut = Builder(dataset);

        Action act = () => sut.Build(Request("cursor"));

        act.Should().Throw<NgbArgumentInvalidException>().WithMessage("*stable keyset cursor*");
    }

    [Fact]
    public void Positive_offset_is_rejected_when_the_dataset_supports_keyset_paging()
    {
        var sut = Builder(CursorDataset());

        Action act = () => sut.Build(Request(offset: 1));

        act.Should().Throw<NgbArgumentInvalidException>()
            .WithMessage("*offset 0*nextCursor*");
    }

    private static PostgresReportCursorColumn Column(string alias)
        => new(alias, "test", ReportSortDirection.Asc, IsHidden: false);

    private static PostgresReportSqlBuilder Builder(PostgresReportDatasetBinding dataset)
        => new(new PostgresReportDatasetCatalog([new Source(dataset)]));

    private static PostgresReportDatasetBinding CursorDataset()
        => new(
            "dataset",
            "rows r",
            [new("id", "r.id", "int64"), new("name", "r.name", "string")],
            [],
            cursorKeyFieldCodes: ["id"]);

    private static PostgresReportExecutionRequest Request(string? cursor = null, int offset = 0)
        => new(
            "dataset",
            [],
            [],
            [new("name", "name_out", "Name", "string")],
            [],
            [new("name", null, ReportSortDirection.Asc)],
            [],
            new Dictionary<string, object?>(),
            new PostgresReportPaging(offset, 10, cursor));

    private sealed class Source(PostgresReportDatasetBinding dataset) : IPostgresReportDatasetSource
    {
        public IReadOnlyList<PostgresReportDatasetBinding> GetDatasets() => [dataset];
    }
}
