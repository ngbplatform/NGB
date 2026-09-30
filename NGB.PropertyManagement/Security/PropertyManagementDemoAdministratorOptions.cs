namespace NGB.PropertyManagement.Security;

public sealed record PropertyManagementDemoAdministratorOptions
{
    public PropertyManagementDemoAdministratorOptions(
        string? authSubject = null,
        string? email = null,
        string? firstName = null,
        string? lastName = null)
    {
        AuthSubject = Normalize(authSubject);
        Email = Normalize(email);
        FirstName = Normalize(firstName);
        LastName = Normalize(lastName);
    }

    public string? AuthSubject { get; }
    public string? Email { get; }
    public string? FirstName { get; }
    public string? LastName { get; }

    public string? DisplayName
    {
        get
        {
            var displayName = string.Join(
                ' ',
                new[] { FirstName, LastName }.Where(static value => value is not null));

            return string.IsNullOrWhiteSpace(displayName) ? null : displayName;
        }
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
