using System.Text;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Contracts.Reporting;
using NGB.Core.Dimensions;
using NGB.Core.Dimensions.Enrichment;
using NGB.Core.Reporting;
using NGB.Persistence.Checkers;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting;
using NGB.Runtime.Reporting.Streaming;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class AccountingConsistencyPagingTests
{
    [Theory]
    [InlineData("!")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task Invalid_continuations_never_reach_the_reader(string json)
    {
        var reader = new Mock<IAccountingConsistencyPageReader>(MockBehavior.Strict);
        var executor = new AccountingConsistencyPagedExecutor(reader.Object, Mock.Of<IAccountingIntegrityDiagnostics>(), Mock.Of<IDimensionSetReader>(), Mock.Of<IDimensionValueEnrichmentReader>(), []);
        var action = () => executor.ExecuteAsync(Definition(), Request(false, false) with { Cursor = json == "!" ? json : Convert.ToBase64String(Encoding.UTF8.GetBytes(json)) }, default);
        await action.Should().ThrowAsync<NgbArgumentInvalidException>().WithMessage("*cursor*");
        reader.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Paging_and_streaming_report_missing_balances_and_broken_chains_consistently(bool hasBalances, bool previous, bool totals)
    {
        var setId = Guid.NewGuid();
        var dimension = Guid.NewGuid();
        var valueId = Guid.NewGuid();
        AccountingConsistencySnapshotRow[] observations =
        [
            new(Guid.NewGuid(), "100", setId, 0, 0, 3, 0, 7, false, true, true),
            new(Guid.NewGuid(), "101", setId, 1, 1, 0, 0, 2, true, false, true),
            new(Guid.NewGuid(), "102", setId, 0, 0, 0, 0, 0, false, false, false)
        ];
        var integrity = new Mock<IAccountingIntegrityDiagnostics>();
        integrity.Setup(x => x.GetTurnoversVsRegisterDiffCountAsync(It.IsAny<DateOnly>(), default)).ReturnsAsync(0);
        var bags = new Dictionary<Guid, DimensionBag> { [setId] = new([new(dimension, valueId)]) };
        var dimensions = new Mock<IDimensionSetReader>();
        dimensions.Setup(x => x.GetBagsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), default)).ReturnsAsync(bags);
        var values = new Mock<IDimensionValueEnrichmentReader>();
        values.Setup(x => x.ResolveAsync(It.IsAny<IReadOnlyCollection<DimensionValueKey>>(), default))
            .ReturnsAsync(new Dictionary<DimensionValueKey, string> { [new(dimension, valueId)] = "North" });
        var streamReader = new Mock<IAccountingConsistencyStreamReader>();
        streamReader.Setup(x => x.HasBalancesAsync(It.IsAny<DateOnly>(), default)).ReturnsAsync(hasBalances);
        streamReader.Setup(x => x.ReadAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly?>(), default)).Returns(Batches());
        var streaming = new AccountingConsistencyStreamingExecutor(integrity.Object, streamReader.Object, dimensions.Object, values.Object);
        var pageReader = new Mock<IAccountingConsistencyPageReader>();
        pageReader.Setup(x => x.ReadPageAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly?>(), null, It.IsAny<int>(), default))
            .ReturnsAsync(new AccountingConsistencyPage(observations, false, null, hasBalances));
        pageReader.Setup(x => x.CountAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly?>(), default))
            .ReturnsAsync(new AccountingConsistencyCounts(0, hasBalances ? 1 : 0, hasBalances && previous ? 2 : 0));
        var request = Request(previous, totals);
        var page = await new AccountingConsistencyPagedExecutor(pageReader.Object, integrity.Object, dimensions.Object, values.Object, [streaming])
            .ExecuteAsync(Definition(), request, default);
        var streamed = new List<ReportRowWrite>();
        await foreach (var row in streaming.ReadAsync(streaming.Prepare(Definition(), request), default)) streamed.Add(row);
        streamed.Select(r => r.Ordinal).Should().Equal(Enumerable.Range(0, streamed.Count));
        var expected = (hasBalances ? 1 : 0) + (hasBalances && previous ? 2 : 0) + (totals ? 5 : 0);
        page.Sheet.Rows.Should().HaveCount(expected);
        streamed.Should().HaveCount(expected);
        if (hasBalances)
            page.Sheet.Rows.First().Cells[4].Display.Should().Contain("North");
        JsonSerializer.Serialize(streamed.Select(r => r.Row)).Should().Be(JsonSerializer.Serialize(page.Sheet.Rows));
        async IAsyncEnumerable<IReadOnlyList<AccountingConsistencySnapshotRow>> Batches()
        {
            await Task.CompletedTask;
            yield return observations;
        }
    }

    private static ReportDefinitionDto Definition() => new(AccountingReportCodes.Consistency, "Consistency");
    private static ReportExecutionRequestDto Request(bool previous, bool totals)
    {
        var parameters = new Dictionary<string, string> { ["period_utc"] = "2026-09-22" };
        if (previous) parameters["previous_period_utc"] = "2026-08-22";
        return new(Parameters: parameters, Layout: totals ? null : new(ShowGrandTotals: false));
    }
}
