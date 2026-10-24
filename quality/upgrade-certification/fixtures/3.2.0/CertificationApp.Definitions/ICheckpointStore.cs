namespace CertificationApp.Definitions;

public interface ICheckpointStore
{
    Task CaptureOnceAsync(Guid operationId, CancellationToken ct);
    Task<int> GetEffectCountAsync(Guid operationId, CancellationToken ct);
}
