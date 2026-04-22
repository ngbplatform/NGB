namespace NGB.Definitions;

/// <summary>
/// A module-level contributor that registers document and catalog type definitions.
/// 
/// IMPORTANT: Implementations must be deterministic and should not perform reflection-based scanning.
/// </summary>
public interface IDefinitionsContributor
{
    void Contribute(DefinitionsBuilder builder);
}
