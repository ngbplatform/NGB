using NGB.Tools.Exceptions;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// Helps prevent tests from hanging forever when using <see cref="Barrier"/>.
/// If any participant fails before reaching the barrier, the test must fail fast.
/// </summary>
internal static class BarrierExtensions
{
    public static void SignalAndWaitOrThrow(this Barrier barrier, TimeSpan timeout)
    {
        if (!barrier.SignalAndWait(timeout))
        {
            var inner = new TimeoutException($"Barrier timed out after {timeout}.");

            throw new NgbTimeoutException(
                operation: "test.barrier.wait",
                innerException: inner,
                additionalContext: new Dictionary<string, object?>
                {
                    ["timeoutMs"] = timeout.TotalMilliseconds,
                    ["participantCount"] = barrier.ParticipantCount,
                    ["participantsRemaining"] = barrier.ParticipantsRemaining
                });
        }
    }
}
