namespace NGB.Core.Security;

public static class NgbSystemPermissions
{
    public static readonly NgbPermissionKey UsersView = new(NgbResourceKinds.System, NgbPermissionResources.Users, NgbPermissionActions.View);
    public static readonly NgbPermissionKey UsersManage = new(NgbResourceKinds.System, NgbPermissionResources.Users, NgbPermissionActions.Manage);
    public static readonly NgbPermissionKey RolesView = new(NgbResourceKinds.System, NgbPermissionResources.Roles, NgbPermissionActions.View);
    public static readonly NgbPermissionKey RolesManage = new(NgbResourceKinds.System, NgbPermissionResources.Roles, NgbPermissionActions.Manage);
    public static readonly NgbPermissionKey PermissionsView = new(NgbResourceKinds.System, NgbPermissionResources.Permissions, NgbPermissionActions.View);
    public static readonly NgbPermissionKey AuditView = new(NgbResourceKinds.System, NgbPermissionResources.Audit, NgbPermissionActions.View);

    public static readonly NgbPermissionKey AttachmentsRead = new(NgbResourceKinds.System, NgbPermissionResources.Attachments, NgbPermissionActions.Read);
    public static readonly NgbPermissionKey AttachmentsCreate = new(NgbResourceKinds.System, NgbPermissionResources.Attachments, NgbPermissionActions.Create);
    public static readonly NgbPermissionKey AttachmentsDelete = new(NgbResourceKinds.System, NgbPermissionResources.Attachments, NgbPermissionActions.Delete);
    public static readonly NgbPermissionKey NotesRead = new(NgbResourceKinds.System, NgbPermissionResources.Notes, NgbPermissionActions.Read);
    public static readonly NgbPermissionKey NotesCreate = new(NgbResourceKinds.System, NgbPermissionResources.Notes, NgbPermissionActions.Create);
    public static readonly NgbPermissionKey NotesUpdate = new(NgbResourceKinds.System, NgbPermissionResources.Notes, NgbPermissionActions.Update);
    public static readonly NgbPermissionKey NotesDelete = new(NgbResourceKinds.System, NgbPermissionResources.Notes, NgbPermissionActions.Delete);

    public static readonly NgbPermissionKey ChartOfAccountsView = new(NgbResourceKinds.Admin, NgbPermissionResources.ChartOfAccounts, NgbPermissionActions.View);
    public static readonly NgbPermissionKey ChartOfAccountsManage = new(NgbResourceKinds.Admin, NgbPermissionResources.ChartOfAccounts, NgbPermissionActions.Manage);
    public static readonly NgbPermissionKey PeriodClosingView = new(NgbResourceKinds.Admin, NgbPermissionResources.PeriodClosing, NgbPermissionActions.View);
    public static readonly NgbPermissionKey PeriodClosingCloseMonth = new(NgbResourceKinds.Admin, NgbPermissionResources.PeriodClosing, NgbPermissionActions.CloseMonth);
    public static readonly NgbPermissionKey PeriodClosingReopenMonth = new(NgbResourceKinds.Admin, NgbPermissionResources.PeriodClosing, NgbPermissionActions.ReopenMonth);
    public static readonly NgbPermissionKey PeriodClosingCloseFiscalYear = new(NgbResourceKinds.Admin, NgbPermissionResources.PeriodClosing, NgbPermissionActions.CloseFiscalYear);
    public static readonly NgbPermissionKey PeriodClosingReopenFiscalYear = new(NgbResourceKinds.Admin, NgbPermissionResources.PeriodClosing, NgbPermissionActions.ReopenFiscalYear);
    public static readonly NgbPermissionKey IntegrityView = new(NgbResourceKinds.Admin, NgbPermissionResources.Integrity, NgbPermissionActions.View);
    public static readonly NgbPermissionKey PostingLogView = new(NgbResourceKinds.Admin, NgbPermissionResources.PostingLog, NgbPermissionActions.View);

    public static IReadOnlyList<NgbPermissionKey> All { get; } =
    [
        UsersView,
        UsersManage,
        RolesView,
        RolesManage,
        PermissionsView,
        AuditView,
        AttachmentsRead,
        AttachmentsCreate,
        AttachmentsDelete,
        NotesRead,
        NotesCreate,
        NotesUpdate,
        NotesDelete,
        ChartOfAccountsView,
        ChartOfAccountsManage,
        PeriodClosingView,
        PeriodClosingCloseMonth,
        PeriodClosingReopenMonth,
        PeriodClosingCloseFiscalYear,
        PeriodClosingReopenFiscalYear,
        IntegrityView,
        PostingLogView
    ];
}
