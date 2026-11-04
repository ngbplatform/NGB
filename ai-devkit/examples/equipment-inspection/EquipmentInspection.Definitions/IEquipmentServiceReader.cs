namespace EquipmentInspection.Definitions;

public sealed record EquipmentServiceDetails(Guid? EquipmentId, decimal? Hours, decimal? Amount, string? Condition);

public interface IEquipmentServiceReader
{
    Task<EquipmentServiceDetails?> ReadAsync(Guid documentId, CancellationToken ct);
}
