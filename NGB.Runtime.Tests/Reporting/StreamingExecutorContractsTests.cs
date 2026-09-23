using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Persistence.Documents;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Streaming;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class StreamingExecutorContractsTests
{
    [Fact]
    public void Stateful_templates_require_initializing_the_stream()
    {
        IStreamingReportExecutor[] executors =
        [
            new AccountCardStreamingExecutor(Mock.Of<IAccountCardEffectiveStreamReader>(), Mock.Of<IAccountCardEffectivePageReader>(), Mock.Of<IDocumentDisplayReader>(), Mock.Of<IAccountByIdResolver>()),
            new GeneralLedgerAggregatedStreamingExecutor(Mock.Of<IGeneralLedgerAggregatedStreamReader>(), Mock.Of<IGeneralLedgerAggregatedSnapshotReader>(), Mock.Of<IDocumentDisplayReader>(), Mock.Of<IAccountByIdResolver>()),
            Planned([])
        ];
        foreach (var executor in executors)
            Assert.Throws<NgbInvariantViolationException>(() => executor.Template(""));
    }

    [Theory]
    [InlineData("missing_sheet")]
    [InlineData("missing_cursor")]
    [InlineData("repeated_cursor")]
    [InlineData("empty_page")]
    public async Task Canonical_streams_reject_missing_sheets_and_nonadvancing_continuations(string failure)
    {
        var specialized = new Mock<IReportSpecializedPlanExecutor>();
        specialized.SetupGet(x => x.ReportCode).Returns("test");
        specialized.Setup(x => x.PrepareExecution(It.IsAny<ReportDefinitionDto>(), It.IsAny<ReportExecutionRequestDto>(), It.IsAny<DateTimeOffset>()))
            .Returns((ReportDefinitionDto _, ReportExecutionRequestDto request, DateTimeOffset _) => request);
        var row = new ReportSheetRowDto(ReportRowKind.Detail, [new(Value: JsonSerializer.SerializeToElement("row"))]);
        specialized.Setup(x => x.ExecuteAsync(It.IsAny<ReportDefinitionDto>(), It.IsAny<ReportExecutionRequestDto>(), default))
            .ReturnsAsync(new ReportDataPage([], [], 0, 1, null, true,
                NextCursor: failure == "missing_cursor" ? null : "same",
                PrebuiltSheet: failure == "missing_sheet" ? null : new([new("value", "Value", "string")], failure == "empty_page" ? [] : [row])));
        var executor = Planned([specialized.Object]);
        var prepared = executor.Prepare(new("test", "Test", Mode: ReportExecutionMode.Canonical), new());
        var action = async () => { await foreach (var _ in executor.ReadAsync(prepared, default)) { } };
        await action.Should().ThrowAsync<NgbInvariantViolationException>();
        if (failure != "missing_sheet") executor.Template(prepared).Columns.Should().ContainSingle();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public async Task Financial_streams_respect_hidden_subtotals_and_totals(int report, bool defaults)
    {
        var reader = new Mock<IAccountingStatementAccountReader>();
        reader.Setup(x => x.ReadAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, default)).Returns(Accounts());
        IStreamingReportExecutor executor = report switch
        {
            0 => new BalanceSheetStreamingExecutor(reader.Object),
            1 => new IncomeStatementStreamingExecutor(reader.Object),
            _ => new EquityStatementStreamingExecutor(reader.Object)
        };
        var request = new ReportExecutionRequestDto(Layout: defaults ? null : new(ShowSubtotals: false, ShowGrandTotals: false), Parameters: Dates());
        var rows = new List<ReportRowWrite>();
        await foreach (var row in executor.ReadAsync(executor.Prepare(new(executor.ReportCode, "Statement"), request), default)) rows.Add(row);
        rows.Should().NotBeEmpty();
        if (defaults) rows.Should().Contain(r => r.Row.RowKind == ReportRowKind.Total);
        else rows.Should().OnlyContain(r => r.Row.RowKind != ReportRowKind.Total && r.Row.RowKind != ReportRowKind.Subtotal);
        async IAsyncEnumerable<AccountingStatementAccount> Accounts()
        {
            await Task.CompletedTask;
            foreach (var section in new[] { StatementSection.Assets, StatementSection.Equity, StatementSection.Income, StatementSection.Expenses })
            {
                yield return new(Guid.NewGuid(), "100", "Account", section, 1, 2, 0, 3);
                if (section == StatementSection.Equity)
                {
                    yield return new(Guid.NewGuid(), "200", "Empty equity", section, 0, 0, 0, 0);
                    yield return new(Guid.NewGuid(), "300", "New equity", section, 0, 2, 0, 2);
                }
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(null)]
    public async Task Trial_balance_stream_preserves_dimension_filters_and_explicit_total_preferences(bool? totals)
    {
        var valueId = Guid.NewGuid();
        var reader = new Mock<ITrialBalanceAccountSummaryReader>(MockBehavior.Strict);
        reader.Setup(x => x.ReadAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
            It.Is<NGB.Core.Dimensions.DimensionScopeBag?>(scopes => totals == null ? scopes == null : scopes != null && scopes.Count == 1 && scopes[0].ValueIds.Contains(valueId)), default)).Returns(Accounts());
        var executor = new TrialBalanceStreamingExecutor(reader.Object);
        var definition = new ReportDefinitionDto(executor.ReportCode, "Trial", Filters: [new("warehouse", "Warehouse", "uuid", Lookup: new NGB.Contracts.Metadata.CatalogLookupSourceDto("demo.warehouse"))]);
        var request = new ReportExecutionRequestDto(Layout: totals == null ? null : new(ShowSubtotals: totals.Value, ShowGrandTotals: totals.Value), Parameters: Dates(),
            Filters: totals == null ? null : new Dictionary<string, ReportFilterValueDto> { ["warehouse"] = new(JsonSerializer.SerializeToElement(valueId)) });
        var rows = new List<ReportRowWrite>();
        await foreach (var row in executor.ReadAsync(executor.Prepare(definition, request), default)) rows.Add(row);
        rows.Count(r => r.Row.RowKind == ReportRowKind.Total).Should().Be(totals != false ? 1 : 0);
        executor.Template(executor.Prepare(definition, request)).Meta!.Title.Should().Be("Trial");
        reader.VerifyAll();
        async IAsyncEnumerable<TrialBalanceAccountSummary> Accounts()
        {
            await Task.CompletedTask;
            yield return new(Guid.NewGuid(), "100", "Cash", AccountType.Asset, 0, 5, 0);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ledger_stream_obeys_total_preferences_when_the_range_is_empty(bool totals)
    {
        var source = new Mock<IGeneralLedgerAggregatedStreamReader>();
        source.Setup(x => x.ReadAsync(It.IsAny<NGB.Accounting.Reports.AccountActivityQuery>(), default)).Returns(Empty());
        var snapshot = new Mock<IGeneralLedgerAggregatedSnapshotReader>();
        snapshot.Setup(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, default)).ReturnsAsync(new GeneralLedgerAggregatedSnapshot("100", 0, 0, 0));
        var documents = new Mock<IDocumentDisplayReader>();
        documents.Setup(x => x.ResolveRefsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), default)).ReturnsAsync(new Dictionary<Guid, DocumentDisplayRef>());
        var accounts = new Mock<IAccountByIdResolver>();
        accounts.Setup(x => x.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), default)).ReturnsAsync(new Dictionary<Guid, Account>());
        var executor = new GeneralLedgerAggregatedStreamingExecutor(source.Object, snapshot.Object, documents.Object, accounts.Object);
        var request = new ReportExecutionRequestDto(Layout: totals ? null : new(ShowGrandTotals: false), Parameters: Dates(), Filters: new Dictionary<string, ReportFilterValueDto> { ["account_id"] = new(JsonSerializer.SerializeToElement(Guid.NewGuid())) });
        var prepared = executor.Prepare(new(executor.ReportCode, "Ledger"), request);
        var rows = new List<ReportRowWrite>();
        await foreach (var row in executor.ReadAsync(prepared, default)) rows.Add(row);
        rows.Should().HaveCount(totals ? 1 : 0);
        executor.Template(prepared).Meta!.Title.Should().Be("Ledger");
        async IAsyncEnumerable<IReadOnlyList<NGB.Accounting.Reports.GeneralLedgerAggregated.GeneralLedgerAggregatedLine>> Empty()
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    [Fact]
    public async Task Empty_account_stream_initializes_a_reusable_template()
    {
        var source = new Mock<IAccountCardEffectiveStreamReader>();
        source.Setup(x => x.ReadAsync(It.IsAny<NGB.Accounting.Reports.AccountActivityQuery>(), default)).Returns(Empty());
        var documents = new Mock<IDocumentDisplayReader>();
        documents.Setup(x => x.ResolveRefsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), default)).ReturnsAsync(new Dictionary<Guid, DocumentDisplayRef>());
        var accounts = new Mock<IAccountByIdResolver>();
        accounts.Setup(x => x.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), default)).ReturnsAsync(new Dictionary<Guid, Account>());
        var executor = new AccountCardStreamingExecutor(source.Object, Mock.Of<IAccountCardEffectivePageReader>(), documents.Object, accounts.Object);
        var request = new ReportExecutionRequestDto(Parameters: Dates(), Filters: new Dictionary<string, ReportFilterValueDto>
        { ["account_id"] = new(JsonSerializer.SerializeToElement(Guid.NewGuid())) });
        var prepared = executor.Prepare(new(executor.ReportCode, "Account"), request);
        var rows = new List<ReportRowWrite>();
        await foreach (var row in executor.ReadAsync(prepared, default)) rows.Add(row);
        rows.Should().BeEmpty();
        executor.Template(prepared).Meta!.Title.Should().Be("Account");
        executor.Template(prepared).Columns.Should().NotBeEmpty();
        async IAsyncEnumerable<IReadOnlyList<NGB.Accounting.Reports.AccountCard.AccountCardLine>> Empty()
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private static Dictionary<string, string> Dates() => new() { ["from_utc"] = "2026-09-01", ["to_utc"] = "2026-09-30", ["as_of_utc"] = "2026-09-30" };
    private static PlannedReportStreamingExecutor Planned(IEnumerable<IReportSpecializedPlanExecutor> executors)
        => new(new(), executors, Mock.Of<IStreamingReportDataSource>(), Mock.Of<IDocumentDisplayReader>(), TimeProvider.System);
}
