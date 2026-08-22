using FluentAssertions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Runtime.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportDefinitionCatalogFullCoverageTests
{
    [Fact]
    public async Task Constructor_FiltersNullSourcesAndEnrichers_EnrichesAndSortsDefinitions()
    {
        var first = Definition("z.report", "Zulu", null);
        var second = Definition("b.report", "Beta", "Accounting");
        var third = Definition("a.report", "Alpha", "Accounting");
        var enricher = new NamingEnricher(" enriched");
        var catalog = new ReportDefinitionCatalog(
            [null!, new StubSource(first, second, third)],
            [null!, enricher]);

        var all = await catalog.GetAllDefinitionsAsync(CancellationToken.None);

        all.Select(definition => definition.ReportCode).Should().Equal("z.report", "a.report", "b.report");
        all.Select(definition => definition.Name).Should().Equal("Zulu enriched", "Alpha enriched", "Beta enriched");
        enricher.CallCount.Should().Be(3);
        (await catalog.GetDefinitionAsync(" A.REPORT ", CancellationToken.None)).Name.Should().Be("Alpha enriched");
    }

    [Fact]
    public void Constructor_WhenEnricherChangesReportCode_ThrowsInvariant()
    {
        var catalog = () => new ReportDefinitionCatalog(
            [new StubSource(Definition("report", "Report", null))],
            [new ChangingCodeEnricher()]);

        catalog.Should().Throw<NgbInvariantViolationException>()
            .WithMessage("*changed report code from 'report' to 'changed'*");
    }

    [Fact]
    public void Constructor_WhenNormalizedCodeIsDuplicated_ThrowsInvariant()
    {
        var catalog = () => new ReportDefinitionCatalog(
            [new StubSource(
                Definition(" report ", "One", null),
                Definition("REPORT", "Two", null))],
            enrichers: null!);

        catalog.Should().Throw<NgbInvariantViolationException>()
            .WithMessage("*Duplicate report code 'report'*");
    }

    [Fact]
    public async Task GetDefinitionAsync_WhenCodeIsUnknown_ThrowsNotFound()
    {
        var catalog = new ReportDefinitionCatalog([]);

        var action = () => catalog.GetDefinitionAsync("missing", CancellationToken.None);

        await action.Should().ThrowAsync<ReportTypeNotFoundException>();
    }

    private static ReportDefinitionDto Definition(string code, string name, string? group)
        => new(ReportCode: code, Name: name, Group: group);

    private sealed class StubSource(params ReportDefinitionDto[] definitions) : IReportDefinitionSource
    {
        public IReadOnlyList<ReportDefinitionDto> GetDefinitions() => definitions;
    }

    private sealed class NamingEnricher(string suffix) : IReportDefinitionEnricher
    {
        public int CallCount { get; private set; }

        public ReportDefinitionDto Enrich(ReportDefinitionDto definition)
        {
            CallCount++;
            return definition with { Name = definition.Name + suffix };
        }
    }

    private sealed class ChangingCodeEnricher : IReportDefinitionEnricher
    {
        public ReportDefinitionDto Enrich(ReportDefinitionDto definition)
            => definition with { ReportCode = "changed" };
    }
}
