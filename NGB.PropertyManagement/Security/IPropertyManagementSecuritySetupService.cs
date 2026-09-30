namespace NGB.PropertyManagement.Security;

/// <summary>
/// Ensures that the Property Management security roles, permissions, and configured
/// demo administrator assignment exist.
/// </summary>
public interface IPropertyManagementSecuritySetupService
{
    Task EnsureDefaultsAsync(CancellationToken ct = default);
}
