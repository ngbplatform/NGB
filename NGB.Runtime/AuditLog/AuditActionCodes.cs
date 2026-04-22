namespace NGB.Runtime.AuditLog;

/// <summary>
/// Stable action codes for the Business AuditLog.
/// These values are part of the external contract (stored in DB).
/// </summary>
public static class AuditActionCodes
{
    // Documents
    public const string DocumentCreateDraft = "document.create_draft";
    public const string DocumentUpdateDraft = "document.update_draft";
    public const string DocumentDeleteDraft = "document.delete_draft";
    public const string DocumentSubmit = "document.submit";
    public const string DocumentApprove = "document.approve";
    public const string DocumentReject = "document.reject";
    public const string DocumentPost = "document.post";
    public const string DocumentUnpost = "document.unpost";
    public const string DocumentRepost = "document.repost";
    public const string DocumentMarkForDeletion = "document.mark_for_deletion";
    public const string DocumentUnmarkForDeletion = "document.unmark_for_deletion";

    // Document Relationships
    public const string DocumentRelationshipCreate = "document.relationship.create";
    public const string DocumentRelationshipDelete = "document.relationship.delete";

    // Catalogs
    public const string CatalogCreate = "catalog.create";
    public const string CatalogUpdate = "catalog.update";
    public const string CatalogMarkForDeletion = "catalog.mark_for_deletion";
    public const string CatalogUnmarkForDeletion = "catalog.unmark_for_deletion";

    // Chart of Accounts
    public const string CoaAccountCreate = "coa.account.create";
    public const string CoaAccountUpdate = "coa.account.update";
    public const string CoaAccountSetActive = "coa.account.set_active";
    public const string CoaAccountMarkForDeletion = "coa.account.mark_for_deletion";
    public const string CoaAccountUnmarkForDeletion = "coa.account.unmark_for_deletion";

    // Periods
    public const string PeriodCloseMonth = "period.close_month";
    public const string PeriodReopenMonth = "period.reopen_month";
    public const string PeriodCloseFiscalYear = "period.close_fiscal_year";
    public const string PeriodReopenFiscalYear = "period.reopen_fiscal_year";

    // Operational Registers
    public const string OperationalRegisterUpsert = "opreg.register.upsert";
    public const string OperationalRegisterReplaceDimensionRules = "opreg.register.dimension_rules.replace";
    public const string OperationalRegisterReplaceResources = "opreg.register.resources.replace";

    // Reference Registers
    public const string ReferenceRegisterUpsert = "refreg.register.upsert";
    public const string ReferenceRegisterReplaceDimensionRules = "refreg.register.dimension_rules.replace";
    public const string ReferenceRegisterReplaceFields = "refreg.register.fields.replace";

    public const string ReferenceRegisterRecordsUpsert = "refreg.records.upsert";
    public const string ReferenceRegisterRecordsTombstone = "refreg.records.tombstone";
}
