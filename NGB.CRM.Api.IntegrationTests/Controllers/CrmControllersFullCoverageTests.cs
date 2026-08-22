using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Metadata;
using NGB.Contracts.Search;
using NGB.CRM.Api.Controllers;
using NGB.CRM.Api.Services;
using NGB.CRM.Contracts;
using NGB.CRM.Runtime;
using NGB.Runtime.Admin;
using NGB.Runtime.Catalogs;
using NGB.Runtime.Documents;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.CRM.Api.IntegrationTests.Controllers;

public sealed class CrmControllersFullCoverageTests : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    [Fact]
    public void Constructors_CreateEveryThinVerticalController()
    {
        var access = Mock.Of<INgbAccessChecker>();
        var securityCache = CreateSecurityCache();
        var catalogs = new PermissionAwareCatalogService(null!, access, securityCache);
        var documents = new PermissionAwareDocumentService(null!, access, securityCache);
        var admin = new PermissionAwareAdminService(null!, access, securityCache);
        using var permissionDefinitions = new PermissionDefinitionRegistry([]);

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
        new SecurityController(
            Mock.Of<ICurrentAccessService>(),
            permissionDefinitions,
            Mock.Of<IUserAccessManagementService>(),
            Mock.Of<IRoleManagementService>(),
            Mock.Of<IEffectiveAccessService>(),
            access).Should().NotBeNull();
        new WorkCenterController(Mock.Of<IWorkCenterQueryService>()).Should().NotBeNull();
    }

    [Fact]
    public async Task AdminApplyDefaults_DelegatesResultAndCancellationToken()
    {
        var expected = new CrmSetupResult(6, 2);
        using var cancellation = new CancellationTokenSource();
        var setup = new Mock<ICrmSetupService>(MockBehavior.Strict);
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
        var service = new CrmCommandPaletteSearchService(
            documents.Object,
            Mock.Of<ICatalogService>(),
            Mock.Of<IReportDefinitionProvider>(),
            _cache,
            NullLogger<CrmCommandPaletteSearchService>.Instance);
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
