namespace NGB.PropertyManagement.Reporting;

public interface IMaintenanceQueueStreamReader
{
    IAsyncEnumerable<IReadOnlyList<MaintenanceQueueRow>> ReadAsync(
        MaintenanceQueueQuery query,
        CancellationToken ct = default);
}
