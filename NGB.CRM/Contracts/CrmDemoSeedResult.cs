namespace NGB.CRM.Contracts;

public sealed record CrmDemoSeedResult(
    DateOnly AsOfUtc,
    int AccountsEnsured,
    int ContactsEnsured,
    int ProductsEnsured,
    int StagesEnsured,
    int DocumentsCreated,
    bool SeededOperationalData);
