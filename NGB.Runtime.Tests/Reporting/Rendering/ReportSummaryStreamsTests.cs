using System.Runtime.CompilerServices;
using FluentAssertions;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting;
using NGB.Runtime.Reporting.Definitions;
using NGB.Runtime.Reporting.Rendering;
using NGB.Runtime.Reporting.Streaming;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting.Rendering;

public sealed class ReportSummaryStreamsTests
{
    [Fact]
    public async Task Premature_summary_end_is_rejected()
    {
        await using var summaries = new ReportSummaryStreams(Plan(), Read, default);
        await ((Func<Task>)(async () => await summaries.NextAsync(0, Values("first")))).Should().ThrowAsync<NgbInvariantViolationException>();
        async IAsyncEnumerable<ReportStreamingDataRow> Read(ReportQueryPlan _, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Groups_use_summary_queries_only_when_measures_are_requested(bool measures)
    {
        var plan = measures ? Plan() : Plan() with { Measures = [] };
        var formatter = new ReportCellFormatter();
        var builder = new ReportGroupTreeBuilder(formatter, new(formatter), new(plan));
        var calls = 0;
        var rows = new List<ReportRowWrite>();
        await foreach (var row in builder.BuildStreamingAsync(plan, ReportSheetBuilder.BuildColumns(plan), Read, default)) rows.Add(row);
        calls.Should().Be(measures ? 3 : 1);
        rows.Should().Contain(r => r.Row.RowKind == ReportRowKind.Group);
        async IAsyncEnumerable<ReportStreamingDataRow> Read(ReportQueryPlan _, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            calls++;
            await Task.CompletedTask;
            yield return new(Values("first"), Values("first"));
        }
    }

    [Fact]
    public async Task Equal_display_labels_do_not_hide_a_mismatched_source_key()
    {
        var plan = Plan();
        var disposed = false;
        await using (var summaries = new ReportSummaryStreams(plan, Read, default))
        {
            var act = () => summaries.NextAsync(0, Values("first"));
            var failure = await act.Should().ThrowAsync<NgbInvariantViolationException>();
            failure.Which.Context.Should().Contain("field", "document_display");
        }
        disposed.Should().BeTrue();

        async IAsyncEnumerable<ReportStreamingDataRow> Read(ReportQueryPlan _, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            try { yield return new(Values("second"), Values("first")); }
            finally { disposed = true; }
        }
    }

    [Fact]
    public async Task Changing_display_at_the_summary_grain_preserves_the_source_key_and_summary_presentation()
    {
        var reads = 0;
        await using var summaries = new ReportSummaryStreams(Plan(), Read, default);
        var first = await summaries.NextAsync(0, Values("first"));
        var second = await summaries.NextAsync(0, Values("second"));
        first.Values["document_display"].Should().Be("Combined documents");
        second.Values["document_display"].Should().Be("Friendly document name");
        reads.Should().Be(1, "one summary stream must serve all groups at the same depth");

        async IAsyncEnumerable<ReportStreamingDataRow> Read(ReportQueryPlan _, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            reads++;
            yield return new(Values("first"), Values("Combined documents"));
            yield return new(Values("second"), Values("Friendly document name"));
        }
    }

    private static Dictionary<string, object?> Values(string value) => new() { ["document_display"] = value };

    private static ReportQueryPlan Plan()
    {
        var definition = new ReportDefinitionRuntimeModel(new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single());
        var layout = new ReportLayoutDto(RowGroups: [new("document_display")], Measures: [new("debit_amount")]);
        return new ReportExecutionPlanner().BuildPlan(new(definition, new(Layout: layout), layout));
    }
}
