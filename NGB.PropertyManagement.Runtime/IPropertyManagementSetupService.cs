using NGB.PropertyManagement.Contracts;

namespace NGB.PropertyManagement.Runtime;

/// <summary>
/// Idempotent initializer for Property Management defaults (accounts, operational registers, policy).
/// Intended to be invoked by an admin "Setup" UI / endpoint.
/// </summary>
public interface IPropertyManagementSetupService
{
    Task<PropertyManagementSetupResult> EnsureDefaultsAsync(CancellationToken ct = default);
}
