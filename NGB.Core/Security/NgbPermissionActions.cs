using NGB.Core.Documents.Actions;

namespace NGB.Core.Security;

public static class NgbPermissionActions
{
    public const string View = "view";
    public const string Manage = "manage";
    public const string Create = "create";
    public const string Edit = "edit";
    public const string Deactivate = "deactivate";
    public const string Reactivate = "reactivate";

    public const string Lookup = "lookup";
    public const string EditDraft = "edit_draft";
    public const string DeleteDraft = "delete_draft";
    public const string MarkForDeletion = StandardDocumentActionCodes.MarkForDeletionValue;
    public const string UnmarkForDeletion = StandardDocumentActionCodes.UnmarkForDeletionValue;
    public const string Post = StandardDocumentActionCodes.PostValue;
    public const string Unpost = StandardDocumentActionCodes.UnpostValue;
    public const string Repost = StandardDocumentActionCodes.RepostValue;
    public const string ViewEffects = StandardDocumentActionCodes.ViewEffectsValue;
    public const string ViewFlow = StandardDocumentActionCodes.ViewFlowValue;
    public const string ViewAudit = StandardDocumentActionCodes.ViewAuditValue;
    public const string Print = StandardDocumentActionCodes.PrintValue;

    public const string Execute = "execute";
    public const string Export = "export";
    public const string SavePrivateVariant = "save_private_variant";
    public const string ManageSharedVariants = "manage_shared_variants";
    public const string DeleteVariant = "delete_variant";
    
    public const string CloseMonth = "close_month";
    public const string ReopenMonth = "reopen_month";
    public const string CloseFiscalYear = "close_fiscal_year";
    public const string ReopenFiscalYear = "reopen_fiscal_year";
}
