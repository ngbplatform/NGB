using NGB.Tools.Extensions;
using NGB.Tools.Normalization;

namespace NGB.OperationalRegisters;

/// <summary>
/// Operational Register identifier.
///
/// Operational Registers are addressed by a stable (code-based) identifier.
/// We use deterministic GUIDs to ensure:
/// - idempotent schema/bootstrap behavior
/// - stable foreign keys from documents/register facts
/// - consistent IDs across environments when the same code is used
///
/// Deterministic format:
///   DeterministicGuid.Create($"OperationalRegister|{code_norm}")
/// where code_norm = lower(trim(code)).
/// </summary>
public static class OperationalRegisterId
{
    public static Guid FromCode(string code)
    {
        var codeNorm = NormalizeCode(code);
        return DeterministicGuid.Create($"OperationalRegister|{codeNorm}");
    }

    public static string NormalizeCode(string code) => CodeNormalizer.NormalizeCodeNorm(code, nameof(code));
}
