namespace NGB.Application.Abstractions.Services;

public interface IReportVariantAccessContext
{
    string? AuthSubject { get; }
    string? Email { get; }
    string? DisplayName { get; }
    bool IsActive { get; }
}
