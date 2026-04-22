namespace NGB.Core.AuditLog;

/// <summary>
/// Audit entity kinds.
///
/// IMPORTANT:
/// - Persisted as SMALLINT in PostgreSQL.
/// - Values are part of the data contract; do not reorder.
/// </summary>
public enum AuditEntityKind : short
{
    Document = 1,
    Catalog = 2,
    ChartOfAccountsAccount = 3,
    Period = 4,
    OperationalRegister = 5,
    DocumentRelationship = 6,
    ReferenceRegister = 7,
}
