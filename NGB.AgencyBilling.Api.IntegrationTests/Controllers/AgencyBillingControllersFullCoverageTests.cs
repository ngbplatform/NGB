using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NGB.AgencyBilling.Api.Controllers;
using NGB.AgencyBilling.Api.Services;
using NGB.AgencyBilling.Contracts;
using NGB.AgencyBilling.Runtime;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Metadata;
using NGB.Contracts.Search;
using NGB.Runtime.Admin;
using NGB.Runtime.Catalogs;
using NGB.Runtime.Documents;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.AgencyBilling.Api.IntegrationTests.Controllers;

public sealed class AgencyBillingControllersFullCoverageTests : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    [Fact]
    public void Constructors_CreateEveryThinVerticalController()
    {
        var access = Mock.Of<INgbAccessChecker>();
        var securityCache = CreateSecurityCache();
        var catalogs = new PermissionAwareCatalogService(Mock.Of<ICatalogService>(), access, securityCache);
        var documents = new PermissionAwareDocumentService(Mock.Of<IDocumentService>(), access, securityCache);
        var admin = new PermissionAwareAdminService(null!, access, securityCache);

        new AdminController(admin).Should().NotBeNull();
        new AuditController(Mock.Of<IAuditLogQueryService>()).Should().NotBeNull();
        new CatalogController(catalogs).Should().NotBeNull();
        new DocumentController(
            documents,
            Mock.Of<IDocumentActionQueryService>(),
            Mock.Of<IDocumentActionDispatcher>()).Should().NotBeNull();
        new ReportController(
            Mock.Of<IReportDefinitionProvider>(),
            Mock.Of<IReportEngine>(),
            Mock.Of<IReportVariantService>(),
            Mock.Of<IReportExportService>(),
            access,
            securityCache).Should().NotBeNull();
        new WorkCenterController(Mock.Of<IWorkCenterQueryService>()).Should().NotBeNull();
    }

    [Fact]
    public async Task AdminApplyDefaults_DelegatesResultAndCancellationToken()
    {
        var expected = new AgencyBillingSetupResult(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            true, false, true, false, true, false, true, false);
        using var cancellation = new CancellationTokenSource();
        var setup = new Mock<IAgencyBillingSetupService>(MockBehavior.Strict);
        setup.Setup(x => x.EnsureDefaultsAsync(cancellation.Token)).ReturnsAsync(expected);
        var sut = new AdminController(new PermissionAwareAdminService(
            null!, Mock.Of<INgbAccessChecker>(), CreateSecurityCache()));

        var result = await sut.ApplyDefaults(setup.Object, cancellation.Token);

        result.Should().BeSameAs(expected);
        setup.VerifyAll();
    }

    [Fact]
    public async Task CommandPaletteSearch_DelegatesRequestAndCancellationToken()
    {
        using var cancellation = new CancellationTokenSource();
        var documents = new Mock<IDocumentService>(MockBehavior.Strict);
        documents.Setup(x => x.GetAllMetadataAsync(cancellation.Token))
            .ReturnsAsync(Array.Empty<DocumentTypeMetadataDto>());
        var service = new AgencyBillingCommandPaletteSearchService(
            documents.Object,
            Mock.Of<ICatalogService>(),
            Mock.Of<IReportDefinitionProvider>(),
            _cache,
            NullLogger<AgencyBillingCommandPaletteSearchService>.Instance);
        var sut = new CommandPaletteController(service);
        var request = new CommandPaletteSearchRequestDto("needle", Scope: "documents");

        var result = await sut.Search(request, cancellation.Token);

        result.Groups.Should().BeEmpty();
        documents.VerifyAll();
    }

    public void Dispose() => _cache.Dispose();

    private NgbSecurityCache CreateSecurityCache()
    {
        var options = new Mock<IOptionsMonitor<NgbSecurityCacheOptions>>(MockBehavior.Strict);
        options.SetupGet(x => x.CurrentValue).Returns(new NgbSecurityCacheOptions());
        return new NgbSecurityCache(_cache, options.Object);
    }
}
