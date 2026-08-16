namespace NGB.CRM.Runtime;

/// <summary>
/// Controls the generated portion of the deterministic CRM demo dataset.
/// Production uses the defaults; tests may select a smaller representative profile.
/// </summary>
public sealed record CrmDemoSeedOptions
{
    public const int ProductionGeneratedAccountCount = 80;
    public const int ProductionGeneratedOpportunityCycleCount = 520;

    public int GeneratedAccountCount { get; init; } = ProductionGeneratedAccountCount;

    public int GeneratedOpportunityCycleCount { get; init; } = ProductionGeneratedOpportunityCycleCount;
}
