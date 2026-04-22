namespace NGB.Persistence.Reporting;

public interface IReportVariantRepository
{
    Task<IReadOnlyList<ReportVariantRecord>> ListVisibleAsync(
        string reportCodeNorm,
        Guid? currentUserId,
        CancellationToken ct);
    
    Task<ReportVariantRecord?> GetVisibleAsync(
        string reportCodeNorm,
        string variantCodeNorm,
        Guid? currentUserId,
        CancellationToken ct);
    
    Task<IReadOnlyList<ReportVariantRecord>> ListByCodeAsync(
        string reportCodeNorm,
        string variantCodeNorm,
        CancellationToken ct);
    
    Task ClearDefaultAsync(
        string reportCodeNorm,
        Guid? ownerPlatformUserId,
        bool isShared,
        string? exceptVariantCodeNorm,
        CancellationToken ct);
    
    Task<ReportVariantRecord> UpsertAsync(ReportVariantRecord record, CancellationToken ct);
    
    Task<bool> DeleteVisibleAsync(
        string reportCodeNorm,
        string variantCodeNorm,
        Guid? currentUserId,
        CancellationToken ct);
}
