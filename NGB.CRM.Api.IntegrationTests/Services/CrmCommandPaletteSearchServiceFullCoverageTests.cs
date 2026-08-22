using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Contracts.Search;
using NGB.Contracts.Services;
using NGB.Core.Reporting;
using NGB.CRM.Api.Services;
using Xunit;

namespace NGB.CRM.Api.IntegrationTests.Services;

public sealed class CrmCommandPaletteSearchServiceFullCoverageTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchAsync_BlankQuery_ReturnsEmptyWithoutCallingProviders(string? query)
    {
        using var fixture = new Fixture();

        var result = await fixture.Sut.SearchAsync(new CommandPaletteSearchRequestDto(query), CancellationToken.None);

        result.Groups.Should().BeEmpty();
        fixture.Documents.VerifyNoOtherCalls();
        fixture.Catalogs.VerifyNoOtherCalls();
        fixture.Reports.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null, 1, 1, 1)]
    [InlineData("", 1, 1, 1)]
    [InlineData(":", 1, 0, 0)]
    [InlineData("document", 1, 0, 0)]
    [InlineData("DOCUMENTS", 1, 0, 0)]
    [InlineData("@", 0, 1, 0)]
    [InlineData("catalog", 0, 1, 0)]
    [InlineData("catalogs", 0, 1, 0)]
    [InlineData("#", 0, 0, 1)]
    [InlineData("report", 0, 0, 1)]
    [InlineData("reports", 0, 0, 1)]
    [InlineData("/", 0, 0, 0)]
    [InlineData("page", 0, 0, 0)]
    [InlineData("pages", 0, 0, 0)]
    [InlineData(">", 0, 0, 0)]
    [InlineData("command", 0, 0, 0)]
    [InlineData("commands", 0, 0, 0)]
    [InlineData("unknown", 0, 0, 0)]
    public async Task SearchAsync_NormalizesScopeAndSkipsEmptyProviders(
        string? scope,
        int documentCalls,
        int catalogCalls,
        int reportCalls)
    {
        using var fixture = new Fixture();

        var result = await fixture.Sut.SearchAsync(
            new CommandPaletteSearchRequestDto("x", $"  {scope}  "),
            CancellationToken.None);

        result.Groups.Should().BeEmpty();
        fixture.Documents.Verify(x => x.GetAllMetadataAsync(It.IsAny<CancellationToken>()), Times.Exactly(documentCalls));
        fixture.Catalogs.Verify(x => x.GetAllMetadataAsync(It.IsAny<CancellationToken>()), Times.Exactly(catalogCalls));
        fixture.Reports.Verify(x => x.GetAllDefinitionsAsync(It.IsAny<CancellationToken>()), Times.Exactly(reportCalls));
    }

    [Fact]
    public async Task SearchAsync_AllProviders_ReturnsRankedDomainResultsAndCachesMetadata()
    {
        using var fixture = new Fixture();
        var documentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var fallbackDocumentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var catalogId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        fixture.SetDocumentMetadata(AllDocumentAliasCodes()
            .Select((code, index) => DocumentMetadata(code, index == 0 ? "  custom-icon  " : " "))
            .Append(DocumentMetadata("accounting.general_journal_entry"))
            .Append(DocumentMetadata("other.ignored")));
        fixture.SetCatalogMetadata(AllCatalogAliasCodes()
            .Select((code, index) => CatalogMetadata(code, index == 0 ? "  catalog-icon  " : null))
            .Append(CatalogMetadata("other.ignored")));
        fixture.SetReports(AllReportIconCodes()
            .Select(code => new ReportDefinitionDto(code, "Match report", " Group ", " Description "))
            .Append(new ReportDefinitionDto(CrmCodes.SalesPipelineReport, "Match empty description", null, " "))
            .Append(new ReportDefinitionDto("crm.custom", "Match custom"))
            .Append(new ReportDefinitionDto("accounting.balance_sheet", "Match accounting"))
            .Append(new ReportDefinitionDto(AccountingReportCodes.PostingLog, "Match excluded"))
            .Append(new ReportDefinitionDto(AccountingReportCodes.Consistency, "Match excluded"))
            .Append(new ReportDefinitionDto("other.ignored", "Match ignored")));

        fixture.SetDocumentHits(
            new DocumentLookupDto(documentId, CrmCodes.Account, "Match display", DocumentStatus.Draft, false, "  MATCH-001  "),
            new DocumentLookupDto(fallbackDocumentId, CrmCodes.Contact, "   ", DocumentStatus.Posted, false, "match-posted"),
            new DocumentLookupDto(Guid.NewGuid(), CrmCodes.Account, "match same", DocumentStatus.Draft, false, "match same"),
            new DocumentLookupDto(Guid.NewGuid(), CrmCodes.Account, "match empty number", DocumentStatus.Draft, false, "   "),
            new DocumentLookupDto(Guid.NewGuid(), CrmCodes.Product, "Match party", DocumentStatus.MarkedForDeletion, true),
            new DocumentLookupDto(Guid.NewGuid(), CrmCodes.OpportunityStage, "Match price", (DocumentStatus)99, false),
            new DocumentLookupDto(Guid.NewGuid(), "crm.unknown", "Match unknown", DocumentStatus.Draft, false));
        fixture.SetCatalogHits(
            new CatalogLookupDto(catalogId, CrmCodes.Account, " Match catalog ", false),
            new CatalogLookupDto(Guid.NewGuid(), CrmCodes.Contact, "Match deleted", true),
            new CatalogLookupDto(Guid.NewGuid(), CrmCodes.Account, null, false),
            new CatalogLookupDto(Guid.NewGuid(), CrmCodes.Product, "   ", false),
            new CatalogLookupDto(Guid.NewGuid(), "crm.unknown", "Match unknown", false));

        var context = new CommandPaletteSearchContextDto(
            EntityType: "other",
            DocumentType: CrmCodes.Account.ToUpperInvariant(),
            CatalogType: CrmCodes.Account.ToUpperInvariant());
        var request = new CommandPaletteSearchRequestDto(" match ", Limit: 99, Context: context);

        var first = await fixture.Sut.SearchAsync(request, CancellationToken.None);
        var second = await fixture.Sut.SearchAsync(request, CancellationToken.None);

        first.Should().BeEquivalentTo(second);
        first.Groups.Select(x => x.Code).Should().Equal("documents", "catalogs", "reports");
        first.Groups.Single(x => x.Code == "documents").Items.Should().Contain(x =>
            x.Key == $"document:{CrmCodes.Account}:{documentId}"
            && x.Title == "crm.account MATCH-001"
            && x.Subtitle == "Match display · Draft"
            && x.Icon == "custom-icon"
            && x.Status == "draft"
            && x.Score == 0.95m);
        first.Groups.Single(x => x.Code == "documents").Items.Should().Contain(x =>
            x.Key == $"document:{CrmCodes.Contact}:{fallbackDocumentId}"
            && x.Subtitle == "Posted");
        first.Groups.Single(x => x.Code == "documents").Items.Should().Contain(x => x.Subtitle!.EndsWith("99", StringComparison.Ordinal));
        first.Groups.Single(x => x.Code == "catalogs").Items.Should().Contain(x =>
            x.Key == $"catalog:{CrmCodes.Account}:{catalogId}"
            && x.Title == "Match catalog"
            && x.Icon == "catalog-icon"
            && x.Status == null
            && x.Score == 0.95m);
        first.Groups.Single(x => x.Code == "catalogs").Items.Should().Contain(x =>
            x.Status == "marked-for-deletion" && x.Subtitle!.Contains("Marked for deletion", StringComparison.Ordinal));
        first.Groups.Single(x => x.Code == "reports").Items.Should().Contain(x => x.Icon == "calendar-check");
        first.Groups.Single(x => x.Code == "reports").Items.Should().Contain(x => x.Icon == "bar-chart");
        first.Groups.Single(x => x.Code == "reports").Items.Should().Contain(x => x.Icon == "file-text");
        first.Groups.Single(x => x.Code == "reports").Items.Should().OnlyContain(x =>
            x.Key != $"report:{AccountingReportCodes.PostingLog}" && x.Key != $"report:{AccountingReportCodes.Consistency}");

        fixture.Documents.Verify(x => x.GetAllMetadataAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.Catalogs.Verify(x => x.GetAllMetadataAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.Reports.Verify(x => x.GetAllDefinitionsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("needle", "needle", 1.0)]
    [InlineData("need", "needle", 0.92)]
    [InlineData("needle", "some-needle-value", 0.78)]
    [InlineData("missing", "needle", 0.0)]
    [InlineData(":", "needle", 0.0)]
    public async Task SearchAsync_CoversScoreBoundaries(string query, string display, double expectedScore)
    {
        using var fixture = new Fixture();
        fixture.SetDocumentMetadata(DocumentMetadata(CrmCodes.Account));
        fixture.SetDocumentHits(new DocumentLookupDto(Guid.NewGuid(), CrmCodes.Account, display, DocumentStatus.Draft, false));

        var result = await fixture.Sut.SearchAsync(
            new CommandPaletteSearchRequestDto(query, "documents", Limit: 1),
            CancellationToken.None);

        if (expectedScore == 0)
        {
            result.Groups.Should().BeEmpty();
        }
        else
        {
            result.Groups.Single().Items.Single().Score.Should().Be((decimal)expectedScore);
        }
    }

    [Fact]
    public async Task SearchAsync_UsesAliasesAndFallbackReportSubtitle()
    {
        using var fixture = new Fixture();
        fixture.SetDocumentMetadata(DocumentMetadata(CrmCodes.Account, label: "Customer"));
        fixture.SetDocumentHits(
            new DocumentLookupDto(Guid.NewGuid(), CrmCodes.Account, null, DocumentStatus.Draft, false),
            new DocumentLookupDto(Guid.NewGuid(), CrmCodes.Account, null, DocumentStatus.Draft, false, " "));
        fixture.SetCatalogMetadata(CatalogMetadata(CrmCodes.Account));
        var catalogId = Guid.NewGuid();
        fixture.SetCatalogHits(new CatalogLookupDto(catalogId, CrmCodes.Account, " ", false));
        fixture.SetReports(new ReportDefinitionDto(CrmCodes.SalesPipelineReport, "account", " ", null));

        var result = await fixture.Sut.SearchAsync(new CommandPaletteSearchRequestDto("account"), CancellationToken.None);

        result.Groups.Single(x => x.Code == "documents").Items.Should().OnlyContain(x =>
            x.Title.StartsWith("Customer ", StringComparison.Ordinal));
        result.Groups.Single(x => x.Code == "catalogs").Items.Single().Title.Should().Be($"{CrmCodes.Account} {catalogId}");
        result.Groups.Single(x => x.Code == "reports").Items.Single().Subtitle.Should().Be("Report");
    }

    [Fact]
    public async Task SearchAsync_NonMatchingReport_ReturnsNoGroup()
    {
        using var fixture = new Fixture();
        fixture.SetReports(new ReportDefinitionDto(
            CrmCodes.SalesPipelineReport,
            "needle",
            " ",
            "description"));

        var result = await fixture.Sut.SearchAsync(
            new CommandPaletteSearchRequestDto("missing", "reports"),
            CancellationToken.None);

        result.Groups.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_NonPositiveLimit_UsesDefault_AndSkipsNonMatchingCatalogHits()
    {
        using var fixture = new Fixture();
        fixture.SetCatalogMetadata(CatalogMetadata(CrmCodes.Account));
        fixture.SetCatalogHits(new CatalogLookupDto(Guid.CreateVersion7(), CrmCodes.Account, "unrelated", false));

        var result = await fixture.Sut.SearchAsync(
            new CommandPaletteSearchRequestDto("definitely-missing", "catalogs", Limit: 0),
            CancellationToken.None);

        result.Groups.Should().BeEmpty();
        fixture.Catalogs.Verify(x => x.LookupAcrossTypesAsync(
            It.IsAny<IReadOnlyList<string>>(),
            "definitely-missing",
            6,
            true,
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_ProviderFailureIsIsolatedAndUncancelledCancellationIsLogged()
    {
        using var fixture = new Fixture();
        fixture.Documents
            .Setup(x => x.GetAllMetadataAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("documents failed"));
        fixture.Catalogs
            .Setup(x => x.GetAllMetadataAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException("not caller cancellation"));
        fixture.SetReports(new ReportDefinitionDto(CrmCodes.SalesPipelineReport, "match"));

        var result = await fixture.Sut.SearchAsync(new CommandPaletteSearchRequestDto("match"), CancellationToken.None);

        result.Groups.Should().ContainSingle().Which.Code.Should().Be("reports");
    }

    [Fact]
    public async Task SearchAsync_CallerCancellationIsPropagated()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        fixture.Documents
            .Setup(x => x.GetAllMetadataAsync(cancellation.Token))
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));

        var action = () => fixture.Sut.SearchAsync(
            new CommandPaletteSearchRequestDto("match", "documents"),
            cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private static IEnumerable<string> AllDocumentAliasCodes()
    {
        yield return CrmCodes.Account;
        yield return CrmCodes.Contact;
        yield return CrmCodes.Product;
        yield return CrmCodes.OpportunityStage;
        yield return CrmCodes.LeadIntake;
        yield return CrmCodes.LeadConversion;
        yield return CrmCodes.Quote;
        yield return CrmCodes.ActivityLog;
        yield return CrmCodes.OpportunityUpdate;
        yield return CrmCodes.LeadQualification;
    }

    private static IEnumerable<string> AllCatalogAliasCodes() => AllDocumentAliasCodes();

    private static IEnumerable<string> AllReportIconCodes()
    {
        yield return CrmCodes.SalesPipelineReport;
        yield return CrmCodes.OpportunityHistoryReport;
        yield return CrmCodes.LeadConversionFunnelReport;
        yield return CrmCodes.ActivitySummaryReport;
        yield return CrmCodes.QuoteRegisterReport;
        yield return CrmCodes.SalesPipelineReport;
    }

    private static DocumentTypeMetadataDto DocumentMetadata(string code, string? icon = null, string? label = null)
        => new(code, label ?? code, EntityKind.Document, icon);

    private static CatalogTypeMetadataDto CatalogMetadata(string code, string? icon = null)
        => new(code, code, EntityKind.Catalog, icon);

    private sealed class Fixture : IDisposable
    {
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());

        public Fixture()
        {
            Documents.Setup(x => x.GetAllMetadataAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<DocumentTypeMetadataDto>());
            Documents.Setup(x => x.LookupAcrossTypesAsync(
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<string?>(),
                    It.IsAny<int>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<DocumentLookupDto>());
            Catalogs.Setup(x => x.GetAllMetadataAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<CatalogTypeMetadataDto>());
            Catalogs.Setup(x => x.LookupAcrossTypesAsync(
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<string?>(),
                    It.IsAny<int>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<CatalogLookupDto>());
            Reports.Setup(x => x.GetAllDefinitionsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<ReportDefinitionDto>());

            Sut = new CrmCommandPaletteSearchService(
                Documents.Object,
                Catalogs.Object,
                Reports.Object,
                _cache,
                NullLogger<CrmCommandPaletteSearchService>.Instance);
        }

        public Mock<IDocumentService> Documents { get; } = new();
        public Mock<ICatalogService> Catalogs { get; } = new();
        public Mock<IReportDefinitionProvider> Reports { get; } = new();
        public CrmCommandPaletteSearchService Sut { get; }

        public void SetDocumentMetadata(params DocumentTypeMetadataDto[] metadata)
            => SetDocumentMetadata((IEnumerable<DocumentTypeMetadataDto>)metadata);

        public void SetDocumentMetadata(IEnumerable<DocumentTypeMetadataDto> metadata)
            => Documents.Setup(x => x.GetAllMetadataAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(metadata.ToArray());

        public void SetCatalogMetadata(params CatalogTypeMetadataDto[] metadata)
            => SetCatalogMetadata((IEnumerable<CatalogTypeMetadataDto>)metadata);

        public void SetCatalogMetadata(IEnumerable<CatalogTypeMetadataDto> metadata)
            => Catalogs.Setup(x => x.GetAllMetadataAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(metadata.ToArray());

        public void SetReports(IEnumerable<ReportDefinitionDto> reports)
            => Reports.Setup(x => x.GetAllDefinitionsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(reports.ToArray());

        public void SetReports(params ReportDefinitionDto[] reports)
            => SetReports((IEnumerable<ReportDefinitionDto>)reports);

        public void SetDocumentHits(params DocumentLookupDto[] hits)
            => Documents.Setup(x => x.LookupAcrossTypesAsync(
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<string?>(),
                    It.IsAny<int>(),
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(hits);

        public void SetCatalogHits(params CatalogLookupDto[] hits)
            => Catalogs.Setup(x => x.LookupAcrossTypesAsync(
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<string?>(),
                    It.IsAny<int>(),
                    true,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(hits);

        public void Dispose() => _cache.Dispose();
    }
}
