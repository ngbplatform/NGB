namespace EquipmentInspection.Definitions;

public sealed record InspectionSource(Guid Id, string Number, Guid EquipmentId, string Result);

public interface IInspectionSourceReader
{
    Task<IReadOnlyList<InspectionSource>> SearchAsync(
        Guid equipmentId, string query, int offset, int limit, CancellationToken ct);

    Task<InspectionSource?> ReadEligibleAsync(Guid id, Guid equipmentId, CancellationToken ct);

    Task<bool> CanPostReinspectionAsync(Guid documentId, CancellationToken ct);
}
