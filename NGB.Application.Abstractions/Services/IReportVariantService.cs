using NGB.Contracts.Reporting;

namespace NGB.Application.Abstractions.Services;

public interface IReportVariantService
{
    Task<IReadOnlyList<ReportVariantDto>> GetAllAsync(string reportCode, CancellationToken ct);
    Task<ReportVariantDto?> GetAsync(string reportCode, string variantCode, CancellationToken ct);
    Task<ReportVariantDto> SaveAsync(ReportVariantDto variant, CancellationToken ct);
    Task DeleteAsync(string reportCode, string variantCode, CancellationToken ct);
}
