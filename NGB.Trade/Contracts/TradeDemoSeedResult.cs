namespace NGB.Trade.Contracts;

/// <summary>
/// Outcome of running Trade demo seed.
/// </summary>
public sealed record TradeDemoSeedResult(
    DateOnly AsOfUtc,
    int WarehousesEnsured,
    int PartnersEnsured,
    int ItemsEnsured,
    int DocumentsCreated,
    bool SeededOperationalData);
