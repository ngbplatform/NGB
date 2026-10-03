namespace NGB.Runtime.Security;

public sealed class NgbAdministratorOptions
{
    public ISet<string> ApplicationRoleCodes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    internal bool IsAdministratorRoleCode(string? code)
        => code is not null && ApplicationRoleCodes.Contains(code.Trim());
}
