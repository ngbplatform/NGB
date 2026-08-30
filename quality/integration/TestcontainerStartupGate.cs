namespace NGB.Testing.Containers;

/// <summary>
/// Coordinates the short Docker startup phase across integration-test processes.
/// Rider and coverage runners can launch several testhost processes at once; serializing
/// container startup prevents concurrent Ryuk handshakes from overwhelming Docker Desktop
/// while preserving parallel execution after each container is ready.
/// </summary>
public static class TestcontainerStartupGate
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    public static async ValueTask<FileStream> AcquireAsync(CancellationToken cancellationToken = default)
    {
        var lockPath = Path.Combine(Path.GetTempPath(), "ngb-testcontainers-docker-startup.lock");
        var timeoutAtUtc = DateTime.UtcNow.Add(DefaultTimeout);
        Exception? lastError = null;

        while (DateTime.UtcNow < timeoutAtUtc)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
            }
            catch (IOException exception)
            {
                lastError = exception;
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }

        throw new TimeoutException(
            $"Timed out after {DefaultTimeout.TotalSeconds:0} seconds waiting to start a Testcontainers resource.",
            lastError);
    }
}
