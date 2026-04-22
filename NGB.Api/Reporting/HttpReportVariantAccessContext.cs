using NGB.Application.Abstractions.Services;

namespace NGB.Api.Reporting;

public sealed class HttpReportVariantAccessContext(ICurrentActorContext currentActorContext)
    : IReportVariantAccessContext
{
    public string? AuthSubject => currentActorContext.Current?.AuthSubject;

    public string? Email => currentActorContext.Current?.Email;

    public string? DisplayName => currentActorContext.Current?.DisplayName;

    public bool IsActive => currentActorContext.Current?.IsActive ?? false;
}
