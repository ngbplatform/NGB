using NGB.Application.Abstractions.Services;
using NGB.Contracts.Services;

namespace NGB.AgencyBilling.Api.IntegrationTests.Support;

/// <summary>
/// Test-data setup adapter. Production callers must execute lifecycle actions through
/// IDocumentActionDispatcher; integration fixtures intentionally use the trusted system port.
/// </summary>
internal static class TestDocumentSystemLifecycleExtensions
{
    public static Task<DocumentDto> PostAsync(this IDocumentService service, string documentType, Guid id, CancellationToken ct)
        => Lifecycle(service).PostAsync(documentType, id, ct);

    public static Task<DocumentDto> UnpostAsync(this IDocumentService service, string documentType, Guid id, CancellationToken ct)
        => Lifecycle(service).UnpostAsync(documentType, id, ct);

    public static Task<DocumentDto> RepostAsync(this IDocumentService service, string documentType, Guid id, CancellationToken ct)
        => Lifecycle(service).RepostAsync(documentType, id, ct);

    public static Task<DocumentDto> MarkForDeletionAsync(this IDocumentService service, string documentType, Guid id, CancellationToken ct)
        => Lifecycle(service).MarkForDeletionAsync(documentType, id, ct);

    public static Task<DocumentDto> UnmarkForDeletionAsync(this IDocumentService service, string documentType, Guid id, CancellationToken ct)
        => Lifecycle(service).UnmarkForDeletionAsync(documentType, id, ct);

    private static IDocumentSystemLifecycleService Lifecycle(IDocumentService service)
        => service as IDocumentSystemLifecycleService
           ?? throw new InvalidOperationException($"{service.GetType().FullName} must implement {nameof(IDocumentSystemLifecycleService)} for trusted test setup.");
}

