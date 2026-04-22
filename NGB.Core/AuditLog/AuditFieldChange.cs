namespace NGB.Core.AuditLog;

/// <summary>
/// Field-level change.
///
/// Values are stored as JSON strings (jsonb in PostgreSQL).
/// Callers are responsible for providing valid JSON payloads.
/// </summary>
public sealed record AuditFieldChange(
    string FieldPath,
    string? OldValueJson,
    string? NewValueJson);
