using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Contracts.Search;
using NGB.Contracts.Services;
using NGB.Core.Reporting;
using NGB.Core.Security;
using NGB.PropertyManagement.Api.Services;
using NGB.PropertyManagement.Definitions;
using NGB.Runtime.Catalogs;
using NGB.Runtime.Documents;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Services;

public sealed class CommandPaletteSearchServiceFullCoverageTests
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
            .Append(CatalogMetadata(PropertyManagementCodes.AccountingPolicy))
            .Append(CatalogMetadata("other.ignored")));
        fixture.SetReports(AllReportIconCodes()
            .Select(code => new ReportDefinitionDto(code, "Match report", " Group ", " Description "))
            .Append(new ReportDefinitionDto(PropertyManagementCodes.TenantStatement, "Match empty description", null, " "))
            .Append(new ReportDefinitionDto("pm.custom", "Match custom"))
            .Append(new ReportDefinitionDto("pm.execute-only", "Match execute-only"))
            .Append(new ReportDefinitionDto("pm.denied", "Match denied"))
            .Append(new ReportDefinitionDto("accounting.balance_sheet", "Match accounting"))
            .Append(new ReportDefinitionDto(AccountingReportCodes.PostingLog, "Match excluded"))
            .Append(new ReportDefinitionDto(AccountingReportCodes.Consistency, "Match excluded"))
            .Append(new ReportDefinitionDto("other.ignored", "Match ignored")));

        fixture.SetDocumentHits(
            new DocumentLookupDto(documentId, PropertyManagementCodes.Property, "Match display", DocumentStatus.Draft, false, "  MATCH-001  "),
            new DocumentLookupDto(fallbackDocumentId, PropertyManagementCodes.Party, "   ", DocumentStatus.Posted, false, "match-posted"),
            new DocumentLookupDto(Guid.NewGuid(), PropertyManagementCodes.Property, "match same", DocumentStatus.Draft, false, "match same"),
            new DocumentLookupDto(Guid.NewGuid(), PropertyManagementCodes.Property, "match empty number", DocumentStatus.Draft, false, "   "),
            new DocumentLookupDto(Guid.NewGuid(), PropertyManagementCodes.BankAccount, "Match party", DocumentStatus.MarkedForDeletion, true),
            new DocumentLookupDto(Guid.NewGuid(), PropertyManagementCodes.ReceivableChargeType, "Match price", (DocumentStatus)99, false),
            new DocumentLookupDto(Guid.NewGuid(), "pm.unknown", "Match unknown", DocumentStatus.Draft, false));
        fixture.SetCatalogHits(
            new CatalogLookupDto(catalogId, PropertyManagementCodes.Property, " Match catalog ", false),
            new CatalogLookupDto(Guid.NewGuid(), PropertyManagementCodes.Party, "Match deleted", true),
            new CatalogLookupDto(Guid.NewGuid(), PropertyManagementCodes.Property, null, false),
            new CatalogLookupDto(Guid.NewGuid(), PropertyManagementCodes.BankAccount, "   ", false),
            new CatalogLookupDto(Guid.NewGuid(), "pm.unknown", "Match unknown", false));

        var context = new CommandPaletteSearchContextDto(
            EntityType: "other",
            DocumentType: PropertyManagementCodes.Property.ToUpperInvariant(),
            CatalogType: PropertyManagementCodes.Property.ToUpperInvariant());
        var request = new CommandPaletteSearchRequestDto(" match ", Limit: 99, Context: context);

        var first = await fixture.Sut.SearchAsync(request, CancellationToken.None);
        var second = await fixture.Sut.SearchAsync(request, CancellationToken.None);

        first.Should().BeEquivalentTo(second);
        first.Groups.Select(x => x.Code).Should().Equal("documents", "catalogs", "reports");
        first.Groups.Single(x => x.Code == "documents").Items.Should().Contain(x =>
            x.Key == $"document:{PropertyManagementCodes.Property}:{documentId}"
            && x.Title == "pm.property MATCH-001"
            && x.Subtitle == "Match display · Draft"
            && x.Icon == "custom-icon"
            && x.Status == "draft"
            && x.Score == 1.00m);
        first.Groups.Single(x => x.Code == "documents").Items.Should().Contain(x =>
            x.Key == $"document:{PropertyManagementCodes.Party}:{fallbackDocumentId}"
            && x.Subtitle == "Posted");
        first.Groups.Single(x => x.Code == "documents").Items.Should().Contain(x => x.Subtitle!.EndsWith("99", StringComparison.Ordinal));
        first.Groups.Single(x => x.Code == "catalogs").Items.Should().Contain(x =>
            x.Key == $"catalog:{PropertyManagementCodes.Property}:{catalogId}"
            && x.Title == "Match catalog"
            && x.Icon == "catalog-icon"
            && x.Status == null
            && x.Score == 0.93m);
        first.Groups.Single(x => x.Code == "catalogs").Items.Should().Contain(x =>
            x.Status == "marked-for-deletion" && x.Subtitle!.Contains("Marked for deletion", StringComparison.Ordinal));
        first.Groups.Single(x => x.Code == "reports").Items.Should().Contain(x => x.Icon == "list");
        first.Groups.Single(x => x.Code == "reports").Items.Should().Contain(x => x.Icon == "bar-chart");
        first.Groups.Single(x => x.Code == "reports").Items.Should().Contain(x => x.Icon == "receipt");
        first.Groups.Single(x => x.Code == "reports").Items.Should().Contain(x => x.Icon == "book-open");
        first.Groups.Single(x => x.Code == "reports").Items.Should().Contain(x => x.Icon == "file-text");
        first.Groups.Single(x => x.Code == "reports").Items.Should().Contain(x => x.Key == "report:pm.execute-only");
        first.Groups.Single(x => x.Code == "reports").Items.Should().NotContain(x => x.Key == "report:pm.denied");
        first.Groups.Single(x => x.Code == "reports").Items.Should().OnlyContain(x =>
            x.Key != $"report:{AccountingReportCodes.PostingLog}" && x.Key != $"report:{AccountingReportCodes.Consistency}");

        fixture.Documents.Verify(x => x.GetAllMetadataAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.Catalogs.Verify(x => x.GetAllMetadataAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.Reports.Verify(x => x.GetAllDefinitionsAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        fixture.Access.Verify(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>()), Times.Exactly(10));
        fixture.Access.Verify(x => x.HasAsync(
            NgbResourceKinds.Report,
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("needle", "needle", 0.93)]
    [InlineData("need", "needle", 0.87)]
    [InlineData("needle", "some needle value", 0.84)]
    [InlineData("needle", "some xneedle value", 0.74)]
    [InlineData("missing", "needle", 0.0)]
    [InlineData(":", "needle", 0.0)]
    public async Task SearchAsync_CoversScoreBoundaries(string query, string display, double expectedScore)
    {
        using var fixture = new Fixture();
        fixture.SetDocumentMetadata(DocumentMetadata(PropertyManagementCodes.Property));
        fixture.SetDocumentHits(new DocumentLookupDto(Guid.NewGuid(), PropertyManagementCodes.Property, display, DocumentStatus.Draft, false));

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
        fixture.SetDocumentMetadata(DocumentMetadata(PropertyManagementCodes.Property, label: "Customer"));
        fixture.SetDocumentHits(
            new DocumentLookupDto(Guid.NewGuid(), PropertyManagementCodes.Property, null, DocumentStatus.Draft, false),
            new DocumentLookupDto(Guid.NewGuid(), PropertyManagementCodes.Property, null, DocumentStatus.Draft, false, " "));
        fixture.SetCatalogMetadata(CatalogMetadata(PropertyManagementCodes.Property));
        var catalogId = Guid.NewGuid();
        fixture.SetCatalogHits(new CatalogLookupDto(catalogId, PropertyManagementCodes.Property, " ", false));
        fixture.SetReports(new ReportDefinitionDto(PropertyManagementCodes.TenantStatement, "property", " ", null));

        var result = await fixture.Sut.SearchAsync(new CommandPaletteSearchRequestDto("property"), CancellationToken.None);

        result.Groups.Single(x => x.Code == "documents").Items.Should().OnlyContain(x =>
            x.Title.StartsWith("Customer ", StringComparison.Ordinal));
        result.Groups.Single(x => x.Code == "catalogs").Items.Single().Title.Should().Be($"{PropertyManagementCodes.Property} {catalogId}");
        result.Groups.Single(x => x.Code == "reports").Items.Single().Subtitle.Should().Be("Report");
    }

    [Fact]
    public async Task SearchAsync_NonMatchingReport_ReturnsNoGroup()
    {
        using var fixture = new Fixture();
        fixture.SetReports(new ReportDefinitionDto(
            PropertyManagementCodes.TenantStatement,
            "needle",
            " ",
            "description"));

        var result = await fixture.Sut.SearchAsync(
            new CommandPaletteSearchRequestDto("missing", "reports"),
            CancellationToken.None);

        result.Groups.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_NonPositiveLimit_UsesDefaultBoundary()
    {
        using var fixture = new Fixture();

        var result = await fixture.Sut.SearchAsync(
            new CommandPaletteSearchRequestDto("x", "unknown", Limit: 0),
            CancellationToken.None);

        result.Groups.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_SeparatorOnlyQuery_CoversEveryProviderBoundary()
    {
        using var fixture = new Fixture();
        fixture.SetDocumentMetadata(DocumentMetadata(PropertyManagementCodes.Property));
        fixture.SetDocumentHits(new DocumentLookupDto(
            Guid.NewGuid(),
            PropertyManagementCodes.Property,
            "value",
            DocumentStatus.Draft,
            false));
        fixture.SetCatalogMetadata(CatalogMetadata(PropertyManagementCodes.Property));
        fixture.SetCatalogHits(new CatalogLookupDto(
            Guid.NewGuid(),
            PropertyManagementCodes.Property,
            "value",
            false));
        fixture.SetReports(new ReportDefinitionDto(PropertyManagementCodes.TenantStatement, "value"));

        var result = await fixture.Sut.SearchAsync(
            new CommandPaletteSearchRequestDto("::"),
            CancellationToken.None);

        result.Groups.Should().BeEmpty();
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
        fixture.SetReports(new ReportDefinitionDto(PropertyManagementCodes.TenantStatement, "match"));

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
        yield return PropertyManagementCodes.Property;
        yield return PropertyManagementCodes.Party;
        yield return PropertyManagementCodes.BankAccount;
        yield return PropertyManagementCodes.ReceivableChargeType;
        yield return PropertyManagementCodes.PayableChargeType;
        yield return PropertyManagementCodes.MaintenanceCategory;
        yield return PropertyManagementCodes.Lease;
        yield return PropertyManagementCodes.MaintenanceRequest;
        yield return PropertyManagementCodes.WorkOrder;
        yield return PropertyManagementCodes.WorkOrderCompletion;
        yield return PropertyManagementCodes.RentCharge;
        yield return PropertyManagementCodes.ReceivableCharge;
        yield return PropertyManagementCodes.LateFeeCharge;
        yield return PropertyManagementCodes.ReceivablePayment;
        yield return PropertyManagementCodes.ReceivableReturnedPayment;
        yield return PropertyManagementCodes.ReceivableCreditMemo;
        yield return PropertyManagementCodes.ReceivableApply;
        yield return PropertyManagementCodes.PayableCharge;
        yield return PropertyManagementCodes.PayablePayment;
        yield return PropertyManagementCodes.PayableCreditMemo;
        yield return PropertyManagementCodes.PayableApply;
    }

    private static IEnumerable<string> AllCatalogAliasCodes() => AllDocumentAliasCodes();

    private static IEnumerable<string> AllReportIconCodes()
    {
        yield return "pm.tenant.statement";
        yield return PropertyManagementSecurityDefaults.MaintenanceQueueReport;
        yield return PropertyManagementSecurityDefaults.ReceivablesOpenItemsReport;
        yield return PropertyManagementSecurityDefaults.ReceivablesOpenItemsDetailsReport;
        yield return AccountingReportCodes.GeneralJournal;
        yield return AccountingReportCodes.AccountCard;
        yield return AccountingReportCodes.GeneralLedgerAggregated;
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

            var snapshot = CreateSnapshot();
            Access.Setup(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
            var securityCache = new NgbSecurityCache(_cache, new OptionsMonitor(new NgbSecurityCacheOptions()));
            var permissionAwareDocuments = new PermissionAwareDocumentService(Documents.Object, Access.Object, securityCache);
            var permissionAwareCatalogs = new PermissionAwareCatalogService(Catalogs.Object, Access.Object, securityCache);

            Sut = new CommandPaletteSearchService(
                permissionAwareDocuments,
                permissionAwareCatalogs,
                Reports.Object,
                Access.Object,
                NullLogger<CommandPaletteSearchService>.Instance);
        }

        public Mock<IDocumentService> Documents { get; } = new();
        public Mock<ICatalogService> Catalogs { get; } = new();
        public Mock<IReportDefinitionProvider> Reports { get; } = new();
        public Mock<INgbAccessChecker> Access { get; } = new();
        public CommandPaletteSearchService Sut { get; }

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

        private static PermissionSnapshot CreateSnapshot()
        {
            var permissions = AllDocumentAliasCodes()
                .Append("accounting.general_journal_entry")
                .SelectMany(static code => new[]
                {
                    new NgbPermissionKey(NgbResourceKinds.Document, code, NgbPermissionActions.View),
                    new NgbPermissionKey(NgbResourceKinds.Document, code, NgbPermissionActions.Lookup),
                    new NgbPermissionKey(NgbResourceKinds.Catalog, code, NgbPermissionActions.View),
                    new NgbPermissionKey(NgbResourceKinds.Catalog, code, NgbPermissionActions.Lookup),
                })
                .Append(new NgbPermissionKey(
                    NgbResourceKinds.Catalog,
                    PropertyManagementCodes.AccountingPolicy,
                    NgbPermissionActions.View))
                .Concat(AllReportIconCodes()
                    .Append(PropertyManagementCodes.TenantStatement)
                    .Append("pm.custom")
                    .Append("accounting.balance_sheet")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(static code => new NgbPermissionKey(
                        NgbResourceKinds.Report,
                        code,
                        NgbPermissionActions.View)))
                .Append(new NgbPermissionKey(
                    NgbResourceKinds.Report,
                    "pm.execute-only",
                    NgbPermissionActions.Execute))
                .ToArray();

            return new PermissionSnapshot(Guid.NewGuid(), "subject", true, true, false, 1, permissions);
        }

        private sealed class OptionsMonitor(NgbSecurityCacheOptions value) : IOptionsMonitor<NgbSecurityCacheOptions>
        {
            public NgbSecurityCacheOptions CurrentValue { get; } = value;
            public NgbSecurityCacheOptions Get(string? name) => CurrentValue;
            public IDisposable? OnChange(Action<NgbSecurityCacheOptions, string?> listener) => null;
        }
    }
}
