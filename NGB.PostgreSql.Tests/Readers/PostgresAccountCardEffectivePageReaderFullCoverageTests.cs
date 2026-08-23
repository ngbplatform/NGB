using System.Data;
using FluentAssertions;
using NGB.Accounting.Reports.AccountCard;
using NGB.Core.Dimensions;
using NGB.Core.Dimensions.Enrichment;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.PostgreSql.Readers;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.Readers;

public sealed class PostgresAccountCardEffectivePageReaderFullCoverageTests
{
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CounterAccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DocumentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid PrimarySetId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid CounterSetId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid DimensionId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid ValueId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly DateTime PeriodUtc = new(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetPage_validates_request_account_range_and_month_boundaries()
    {
        var sut = Fixture().Reader;
        Func<Task> missingRequest = () => sut.GetPageAsync(null!);
        Func<Task> missingAccount = () => sut.GetPageAsync(Request(accountId: Guid.Empty));
        Func<Task> reversed = () => sut.GetPageAsync(Request(from: new(2026, 9, 1), to: new(2026, 8, 1)));
        Func<Task> invalidFrom = () => sut.GetPageAsync(Request(from: new(2026, 8, 2)));
        Func<Task> invalidTo = () => sut.GetPageAsync(Request(to: new(2026, 9, 2)));

        await missingRequest.Should().ThrowAsync<NgbArgumentRequiredException>();
        await missingAccount.Should().ThrowAsync<NgbArgumentRequiredException>();
        await reversed.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await invalidFrom.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await invalidTo.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Page_only_without_paging_uses_unbounded_sql_and_empty_resolution_fast_paths()
    {
        var fixture = Fixture();

        var page = await fixture.Reader.GetPageAsync(Request(disablePaging: true));

        page.Lines.Should().BeEmpty();
        page.HasMore.Should().BeFalse();
        page.NextCursor.Should().BeNull();
        fixture.Dimensions.Calls.Should().Be(0);
        fixture.Enrichment.Calls.Should().Be(0);
        var sql = fixture.Connection.Commands.Should().ContainSingle().Subject.CommandText;
        sql.Should().Contain("FROM effective_lines");
        sql.Should().NotContain("LIMIT @LimitPlusOne");
        sql.Should().NotContain("MATERIALIZED");
    }

    [Fact]
    public async Task Page_only_with_paging_and_no_cursor_binds_null_cursor_values()
    {
        var fixture = Fixture();

        var page = await fixture.Reader.GetPageAsync(Request(pageSize: 1));

        page.Lines.Should().BeEmpty();
        var command = fixture.Connection.Commands.Should().ContainSingle().Subject;
        command.CommandText.Should().NotContain("@AfterPeriodUtc");
        command.CommandText.Should().Contain("LIMIT @LimitPlusOne");
        command.ParametersSnapshot.Single(parameter => parameter.ParameterName == "AfterPeriodUtc").Value
            .Should().Be(DBNull.Value);
        command.ParametersSnapshot.Single(parameter => parameter.ParameterName == "AfterEntryId").Value
            .Should().Be(DBNull.Value);
    }

    [Fact]
    public async Task Page_only_with_scope_and_cursor_trims_lookahead_and_resolves_present_and_missing_dimension_sets()
    {
        var first = LineRow(1, PrimarySetId, CounterSetId);
        var second = LineRow(2, Guid.Parse("88888888-8888-8888-8888-888888888888"), CounterSetId);
        var bag = new DimensionBag([new DimensionValue(DimensionId, ValueId)]);
        var fixture = Fixture(
            [first, second],
            new Dictionary<Guid, DimensionBag> { [PrimarySetId] = bag, [CounterSetId] = bag },
            new Dictionary<DimensionValueKey, string> { [new(DimensionId, ValueId)] = "Head office" });
        var request = Request(
            pageSize: 1,
            cursor: new AccountCardLineCursor { AfterPeriodUtc = PeriodUtc.AddDays(-1), AfterEntryId = 0 },
            scopes: new DimensionScopeBag([new DimensionScope(DimensionId, [ValueId])]));

        var page = await fixture.Reader.GetPageAsync(request);

        page.HasMore.Should().BeTrue();
        page.Lines.Should().ContainSingle();
        page.NextCursor!.AfterEntryId.Should().Be(1);
        page.Lines[0].Dimensions.Should().BeSameAs(bag);
        page.Lines[0].CounterAccountDimensions.Should().BeSameAs(bag);
        page.Lines[0].DimensionValueDisplays[DimensionId].Should().Be("Head office");
        page.Lines[0].CounterAccountDimensionValueDisplays[DimensionId].Should().Be("Head office");
        fixture.Dimensions.LastIds.Should().BeEquivalentTo(new[] { PrimarySetId, CounterSetId });
        fixture.Enrichment.Calls.Should().Be(1);
        var sql = fixture.Connection.Commands.Should().ContainSingle().Subject.CommandText;
        sql.Should().Contain("requested_scope_pairs");
        sql.Should().Contain("@AfterPeriodUtc");
        sql.Should().Contain("LIMIT @LimitPlusOne");
    }

    [Fact]
    public async Task Page_with_a_line_defaults_both_unresolved_dimension_sets()
    {
        var fixture = Fixture([LineRow(1, PrimarySetId, CounterSetId)]);

        var page = await fixture.Reader.GetPageAsync(Request());

        var line = page.Lines.Should().ContainSingle().Subject;
        line.Dimensions.Should().BeSameAs(DimensionBag.Empty);
        line.CounterAccountDimensions.Should().BeSameAs(DimensionBag.Empty);
        line.DimensionValueDisplays.Should().BeEmpty();
        line.CounterAccountDimensionValueDisplays.Should().BeEmpty();
    }

    [Fact]
    public async Task Totals_without_paging_returns_zero_for_no_rows_and_materializes_unbounded_totals_sql()
    {
        var fixture = Fixture();

        var page = await fixture.Reader.GetPageAsync(Request(includeTotals: true, disablePaging: true));

        page.Lines.Should().BeEmpty();
        page.TotalDebit.Should().Be(0);
        page.TotalCredit.Should().Be(0);
        page.HasMore.Should().BeFalse();
        var sql = fixture.Connection.Commands.Should().ContainSingle().Subject.CommandText;
        sql.Should().Contain("effective_lines AS\nMATERIALIZED");
        sql.Should().Contain("FALSE AS \"HasMore\"");
        sql.Should().NotContain("LIMIT @PageSize");
    }

    [Fact]
    public async Task Totals_row_without_entry_preserves_totals_and_handles_inconsistent_has_more_defensively()
    {
        var fixture = Fixture([TotalsOnlyRow(120m, 30m, hasMore: true)]);

        var page = await fixture.Reader.GetPageAsync(Request(includeTotals: true, pageSize: 1));

        page.Lines.Should().BeEmpty();
        page.TotalDebit.Should().Be(120);
        page.TotalCredit.Should().Be(30);
        page.HasMore.Should().BeTrue();
        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task Totals_paging_with_cursor_maps_line_totals_and_next_cursor()
    {
        var fixture = Fixture([TotalsLineRow(9, 120m, 30m, hasMore: true)]);
        var request = Request(
            includeTotals: true,
            pageSize: 1,
            cursor: new AccountCardLineCursor { AfterPeriodUtc = PeriodUtc.AddDays(-1), AfterEntryId = 5 });

        var page = await fixture.Reader.GetPageAsync(request);

        page.Lines.Should().ContainSingle();
        page.Lines[0].EntryId.Should().Be(9);
        page.Lines[0].DebitAmount.Should().Be(12);
        page.TotalDebit.Should().Be(120);
        page.TotalCredit.Should().Be(30);
        page.HasMore.Should().BeTrue();
        page.NextCursor!.AfterEntryId.Should().Be(9);
        var sql = fixture.Connection.Commands.Should().ContainSingle().Subject.CommandText;
        sql.Should().Contain("paged_raw");
        sql.Should().Contain("@AfterEntryId");
        sql.Should().Contain("LIMIT @PageSize");
    }

    private static AccountCardLinePageRequest Request(
        Guid? accountId = null,
        DateOnly? from = null,
        DateOnly? to = null,
        int pageSize = 20,
        AccountCardLineCursor? cursor = null,
        DimensionScopeBag? scopes = null,
        bool includeTotals = false,
        bool disablePaging = false)
        => new()
        {
            AccountId = accountId ?? AccountId,
            FromInclusive = from ?? new DateOnly(2026, 8, 1),
            ToInclusive = to ?? new DateOnly(2026, 9, 1),
            PageSize = pageSize,
            Cursor = cursor,
            DimensionScopes = scopes,
            IncludeTotals = includeTotals,
            DisablePaging = disablePaging
        };

    private static FixtureState Fixture(
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? rows = null,
        IReadOnlyDictionary<Guid, DimensionBag>? bags = null,
        IReadOnlyDictionary<DimensionValueKey, string>? displays = null)
        => new(rows ?? [], bags ?? new Dictionary<Guid, DimensionBag>(), displays ?? new Dictionary<DimensionValueKey, string>());

    private static IReadOnlyDictionary<string, object?> LineRow(long entryId, Guid primarySetId, Guid counterSetId)
        => new Dictionary<string, object?>
        {
            ["EntryId"] = entryId,
            ["PeriodUtc"] = PeriodUtc.AddMinutes(entryId),
            ["DocumentId"] = DocumentId,
            ["AccountId"] = AccountId,
            ["AccountCode"] = "1000",
            ["CounterAccountId"] = CounterAccountId,
            ["CounterAccountCode"] = "2000",
            ["CounterAccountDimensionSetId"] = counterSetId,
            ["DimensionSetId"] = primarySetId,
            ["DebitAmount"] = 12m,
            ["CreditAmount"] = 0m
        };

    private static IReadOnlyDictionary<string, object?> TotalsLineRow(
        long entryId,
        decimal totalDebit,
        decimal totalCredit,
        bool hasMore)
    {
        var row = new Dictionary<string, object?>(LineRow(entryId, PrimarySetId, CounterSetId))
        {
            ["TotalDebit"] = totalDebit,
            ["TotalCredit"] = totalCredit,
            ["HasMore"] = hasMore
        };
        return row;
    }

    private static IReadOnlyDictionary<string, object?> TotalsOnlyRow(
        decimal totalDebit,
        decimal totalCredit,
        bool hasMore)
        => new Dictionary<string, object?>
        {
            ["EntryId"] = null,
            ["TotalDebit"] = totalDebit,
            ["TotalCredit"] = totalCredit,
            ["HasMore"] = hasMore
        };

    private sealed class FixtureState(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyDictionary<Guid, DimensionBag> bags,
        IReadOnlyDictionary<DimensionValueKey, string> displays)
    {
        public RecordingDbConnection Connection { get; } = new(readerFactory: _ => Rows(rows));
        public StubDimensionSetReader Dimensions { get; } = new(bags);
        public StubEnrichmentReader Enrichment { get; } = new(displays);

        public PostgresAccountCardEffectivePageReader Reader => new(
            new RecordingUnitOfWork(Connection),
            Dimensions,
            Enrichment);
    }

    private sealed class StubDimensionSetReader(IReadOnlyDictionary<Guid, DimensionBag> bags) : IDimensionSetReader
    {
        public int Calls { get; private set; }
        public IReadOnlyCollection<Guid> LastIds { get; private set; } = [];

        public Task<IReadOnlyDictionary<Guid, DimensionBag>> GetBagsByIdsAsync(
            IReadOnlyCollection<Guid> dimensionSetIds,
            CancellationToken ct = default)
        {
            Calls++;
            LastIds = dimensionSetIds;
            return Task.FromResult(bags);
        }
    }

    private sealed class StubEnrichmentReader(IReadOnlyDictionary<DimensionValueKey, string> displays)
        : IDimensionValueEnrichmentReader
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyDictionary<DimensionValueKey, string>> ResolveAsync(
            IReadOnlyCollection<DimensionValueKey> keys,
            CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(displays);
        }
    }

    private static System.Data.Common.DbDataReader Rows(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var table = new DataTable();
        foreach (var column in rows.SelectMany(row => row.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
            table.Columns.Add(column, typeof(object));

        foreach (var values in rows)
        {
            var row = table.NewRow();
            foreach (DataColumn column in table.Columns)
                row[column] = values.TryGetValue(column.ColumnName, out var value) && value is not null
                    ? value
                    : DBNull.Value;
            table.Rows.Add(row);
        }

        return table.CreateDataReader();
    }
}
