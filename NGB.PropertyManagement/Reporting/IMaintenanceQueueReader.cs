namespace NGB.PropertyManagement.Reporting;

public interface IMaintenanceQueueReader
{
    Task<MaintenanceQueuePage> GetPageAsync(MaintenanceQueueQuery query, CancellationToken ct = default);
}
