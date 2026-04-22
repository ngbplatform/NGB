using System.Security.Cryptography;
using System.Text;
using NGB.Tools.Exceptions;

namespace NGB.Tools.Extensions;

/// <summary>
/// Creates deterministic GUIDs from stable string inputs.
///
/// Why:
/// - Some operations (e.g., fiscal year close) must be idempotent by business semantics.
/// - PostingEngine idempotency is keyed by (document_id, operation) in accounting_posting_state.
/// - Using deterministic document_id allows us to make such operations retry-safe and double-run safe.
///
/// Notes:
/// - This is NOT a UUIDv5 implementation; it's a simple "hash -> GUID" helper.
/// - Collision risk is practically negligible for our scope.
/// - Input MUST be stable across runs (use explicit formats, avoid culture-sensitive strings).
/// </summary>
public static class DeterministicGuid
{
    public static Guid Create(string stableInput)
    {
        if (string.IsNullOrWhiteSpace(stableInput))
            throw new NgbArgumentRequiredException(nameof(stableInput));

        // SHA256 => 32 bytes. We take first 16 bytes for Guid.
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(stableInput));

        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guidBytes);

        // Make it look like RFC 4122 variant, version 5-ish (just for readability/consistency).
        // Version: 5 (0101)
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x50);
        // Variant: 10xx
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);

        return new Guid(guidBytes);
    }
}
