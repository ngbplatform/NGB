using NGB.Application.Abstractions.Services;

namespace NGB.Runtime.Reporting;

public sealed class NullReportVariantAccessContext : IReportVariantAccessContext
{
    public string? AuthSubject => null;

    public string? Email => null;

    public string? DisplayName => null;

    public bool IsActive => false;
}
