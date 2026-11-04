namespace EquipmentInspection.Definitions;

public sealed record EquipmentConditionDetails(Guid? EquipmentId, string? Condition);

public interface IEquipmentConditionReader
{
    Task<EquipmentConditionDetails?> ReadAsync(Guid documentId, CancellationToken ct);
}
