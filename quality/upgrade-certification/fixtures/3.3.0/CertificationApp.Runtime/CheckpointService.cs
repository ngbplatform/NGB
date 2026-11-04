using CertificationApp.Definitions;

namespace CertificationApp.Runtime;

public sealed class CheckpointService(ICheckpointStore store)
{
    public Task CaptureAsync(Guid operationId, CancellationToken ct)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("A checkpoint operation ID is required.", nameof(operationId));

        return store.CaptureOnceAsync(operationId, ct);
    }

    public Task<int> GetEffectCountAsync(Guid operationId, CancellationToken ct)
        => store.GetEffectCountAsync(operationId, ct);
}
