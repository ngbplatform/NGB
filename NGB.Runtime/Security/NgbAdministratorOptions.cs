namespace NGB.Runtime.Security;

public sealed class NgbAdministratorOptions
{
    public ISet<string> ApplicationRoleCodes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
