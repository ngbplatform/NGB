namespace EquipmentInspection.Definitions;

public sealed record EquipmentUsageDetails(Guid? EquipmentId, decimal? Hours);

public interface IEquipmentUsageReader
{
    Task<EquipmentUsageDetails?> ReadAsync(Guid documentId, CancellationToken ct);
}
