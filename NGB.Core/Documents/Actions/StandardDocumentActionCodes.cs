namespace NGB.Core.Documents.Actions;

public static class StandardDocumentActionCodes
{
    public const string PostValue = "post";
    public const string UnpostValue = "unpost";
    public const string RepostValue = "repost";
    public const string MarkForDeletionValue = "mark_for_deletion";
    public const string UnmarkForDeletionValue = "unmark_for_deletion";
    public const string ViewEffectsValue = "view_effects";
    public const string ViewFlowValue = "view_flow";
    public const string ViewAuditValue = "view_audit";
    public const string PrintValue = "print";
    
    public const string DocumentActionCompletedType = "ngb.document.action.completed";
    public const string DocumentType = "documentType";
    public const string DocumentTypeCode = "documentTypeCode";
    public const string DocumentEditorCode = "document.editor";
    public const string DocumentIdKey = "documentId";
    public const string DocumentActionCode = "actionCode";
    
    public static readonly DocumentActionCode Post = new(PostValue);
    public static readonly DocumentActionCode Unpost = new(UnpostValue);
    public static readonly DocumentActionCode Repost = new(RepostValue);
    public static readonly DocumentActionCode MarkForDeletion = new(MarkForDeletionValue);
    public static readonly DocumentActionCode UnmarkForDeletion = new(UnmarkForDeletionValue);
    public static readonly DocumentActionCode ViewEffects = new(ViewEffectsValue);
    public static readonly DocumentActionCode ViewFlow = new(ViewFlowValue);
    public static readonly DocumentActionCode ViewAudit = new(ViewAuditValue);
    public static readonly DocumentActionCode Print = new(PrintValue);
}
