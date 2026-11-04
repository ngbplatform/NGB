namespace EquipmentInspection.Definitions;

public sealed record ChecklistSummary(bool? Reviewed, long LineCount, long IncompleteCount);

public interface IChecklistReader
{
    Task<ChecklistSummary?> ReadAsync(Guid documentId, CancellationToken ct);
}
