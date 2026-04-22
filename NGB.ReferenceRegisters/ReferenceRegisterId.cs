using NGB.Tools.Extensions;
using NGB.Tools.Normalization;

namespace NGB.ReferenceRegisters;

/// <summary>
/// Reference Register identifier.
///
/// Reference Registers store time-effective facts.
/// We address registers by a stable, code-based deterministic identifier.
///
/// Deterministic format:
///   DeterministicGuid.Create($"ReferenceRegister|{code_norm}")
/// where code_norm = lower(trim(code)).
/// </summary>
public static class ReferenceRegisterId
{
    public static Guid FromCode(string code)
    {
        var codeNorm = NormalizeCode(code);
        return DeterministicGuid.Create($"ReferenceRegister|{codeNorm}");
    }

    public static string NormalizeCode(string code) => CodeNormalizer.NormalizeCodeNorm(code, nameof(code));
}
