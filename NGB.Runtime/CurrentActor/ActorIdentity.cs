namespace NGB.Runtime.CurrentActor;

/// <summary>
/// Identity data for the current execution actor.
/// <para>
/// In production, this is typically sourced from the request context (Keycloak subject, email, display name).
/// </para>
/// </summary>
public sealed record ActorIdentity(
    string AuthSubject,
    string? Email,
    string? DisplayName,
    bool IsActive = true);
