using Microsoft.Extensions.DependencyInjection;
using Moq;
using NGB.Application.Abstractions.BusinessObjects;
using NGB.Application.Abstractions.Features;
using NGB.Application.Abstractions.Services;
using NGB.Attachments;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Core.Security;
using NGB.Mcp;
using NGB.Notes;
using NGB.Runtime.Security;

namespace NGB.Testing.Mcp;

internal static class McpTestServices
{
    internal static ServiceCollection Create(bool admin = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNgbMcpReadTools();
        services.AddScoped(_ => new TestActor { Snapshot = Snapshot(admin) });
        services.AddScoped<INgbAccessChecker>(provider =>
        {
            var actor = provider.GetRequiredService<TestActor>();
            var access = new Mock<INgbAccessChecker>();
            access.Setup(value => value.GetSnapshotAsync(It.IsAny<CancellationToken>())).Returns(() => Task.FromResult(actor.Snapshot));
            return access.Object;
        });
        AddReads(services);
        return services;
    }

    internal static void AddReads(IServiceCollection services)
    {
        services.AddSingleton<PermissionDefinitionRegistry>();
        services.AddSingleton<INgbPermissionDefinitionSource, PlatformPermissionDefinitionSource>();
        var catalogs = new Mock<IAuthorizedCatalogReadService>(MockBehavior.Strict);
        catalogs.Setup(value => value.GetAllMetadataAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<CatalogTypeMetadataDto>());
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        documents.Setup(value => value.GetAllMetadataAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<DocumentTypeMetadataDto>());
        var reports = new Mock<IAuthorizedReportReadService>(MockBehavior.Strict);
        reports.Setup(value => value.GetAllDefinitionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ReportDefinitionDto>());
        services.AddScoped(_ => catalogs.Object);
        services.AddScoped(_ => documents.Object);
        services.AddScoped(_ => reports.Object);
        services.AddScoped(_ => Mock.Of<IAuthorizedGeneralJournalEntryReadService>());
        services.AddScoped(_ => Mock.Of<IRecordReadScopeFactory>());
        services.AddScoped(_ => Mock.Of<IReportRequestAdmission>());
        services.AddScoped(_ => Mock.Of<IAuditLogQueryService>());
        services.AddScoped(_ => Mock.Of<IWorkCenterQueryService>());
        services.AddScoped(_ => Mock.Of<IBusinessObjectContentSummaryService>());
        services.AddScoped(_ => Mock.Of<INoteService>());
        services.AddScoped(_ => Mock.Of<IAttachmentService>());
        services.AddScoped(_ => Mock.Of<INgbFeatureService>());
        services.AddScoped(_ => Mock.Of<INgbSearchService>());
    }

    internal static PermissionSnapshot Snapshot(bool admin, params NgbPermissionKey[] permissions)
        => new(Guid.NewGuid(), "test-user", true, true, admin, 0, permissions);

    internal sealed class TestActor
    {
        internal PermissionSnapshot Snapshot { get; set; } = PermissionSnapshot.Anonymous;
    }
}
