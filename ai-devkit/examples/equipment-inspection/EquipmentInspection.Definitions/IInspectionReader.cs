namespace EquipmentInspection.Definitions;

public sealed record InspectionDetails(Guid? EquipmentId, string? Result);

public interface IInspectionReader
{
    Task<InspectionDetails?> ReadAsync(Guid documentId, CancellationToken ct);
}
