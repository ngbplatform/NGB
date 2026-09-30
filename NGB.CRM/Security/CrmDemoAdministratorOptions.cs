namespace NGB.CRM.Security;

public sealed record CrmDemoAdministratorOptions
{
    public const string DefaultAuthSubject = "6d49204b-867c-4180-a30d-a5e290e13c73";
    public const string DefaultEmail = "alex.carter@demo.ngbplatform.com";
    public const string DefaultFirstName = "Alex";
    public const string DefaultLastName = "Carter";

    public CrmDemoAdministratorOptions(
        string? authSubject = null,
        string? email = null,
        string? firstName = null,
        string? lastName = null)
    {
        AuthSubject = NormalizeOrDefault(authSubject, DefaultAuthSubject);
        Email = NormalizeOrDefault(email, DefaultEmail);
        FirstName = NormalizeOrDefault(firstName, DefaultFirstName);
        LastName = NormalizeOrDefault(lastName, DefaultLastName);
    }

    public string AuthSubject { get; }
    public string Email { get; }
    public string FirstName { get; }
    public string LastName { get; }

    private static string NormalizeOrDefault(string? value, string fallback)
        => value is null ? fallback : value.Trim();
}
