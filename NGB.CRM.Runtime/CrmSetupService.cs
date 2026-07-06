using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.CRM.Contracts;
using NGB.Contracts.Common;
using NGB.Contracts.Security;
using NGB.Contracts.Services;
using NGB.Core.Security;
using NGB.Metadata.Base;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.ReferenceRegisters;
using NGB.Runtime.Security;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Extensions;

namespace NGB.CRM.Runtime;

public sealed class CrmSetupService(
    IReferenceRegisterManagementService refregManagement,
    IReferenceRegisterAdminMaintenanceService refregMaintenance,
    ICatalogService catalogs,
    IRoleManagementService roles,
    IUnitOfWork uow,
    IPlatformUserRepository platformUsers,
    IPlatformUserRoleRepository userRoles,
    IUserAccessVersionRepository userAccessVersions)
    : ICrmSetupService
{
    public async Task<CrmSetupResult> EnsureDefaultsAsync(CancellationToken ct = default)
    {
        await EnsureReferenceRegistersAsync(ct);

        var stages = 0;
        stages += await EnsureCatalogAsync(CrmCodes.OpportunityStage, "Prospecting", Payload(new
        {
            display = "Prospecting",
            stage_code = "PROSPECTING",
            name = "Prospecting",
            ordinal = 10,
            default_probability = 10m,
            is_closed = false,
            is_won = false,
            is_active = true
        }), ct, matchField: "stage_code", matchValue: "PROSPECTING");
        stages += await EnsureCatalogAsync(CrmCodes.OpportunityStage, "Qualification", Payload(new
        {
            display = "Qualification",
            stage_code = "QUALIFICATION",
            name = "Qualification",
            ordinal = 20,
            default_probability = 25m,
            is_closed = false,
            is_won = false,
            is_active = true
        }), ct, matchField: "stage_code", matchValue: "QUALIFICATION");
        stages += await EnsureCatalogAsync(CrmCodes.OpportunityStage, "Proposal", Payload(new
        {
            display = "Proposal",
            stage_code = "PROPOSAL",
            name = "Proposal",
            ordinal = 30,
            default_probability = 50m,
            is_closed = false,
            is_won = false,
            is_active = true
        }), ct, matchField: "stage_code", matchValue: "PROPOSAL");
        stages += await EnsureCatalogAsync(CrmCodes.OpportunityStage, "Negotiation", Payload(new
        {
            display = "Negotiation",
            stage_code = "NEGOTIATION",
            name = "Negotiation",
            ordinal = 40,
            default_probability = 75m,
            is_closed = false,
            is_won = false,
            is_active = true
        }), ct, matchField: "stage_code", matchValue: "NEGOTIATION");
        stages += await EnsureCatalogAsync(CrmCodes.OpportunityStage, "Closed Won", Payload(new
        {
            display = "Closed Won",
            stage_code = "CLOSED_WON",
            name = "Closed Won",
            ordinal = 90,
            default_probability = 100m,
            is_closed = true,
            is_won = true,
            is_active = true
        }), ct, matchField: "stage_code", matchValue: "CLOSED_WON");
        stages += await EnsureCatalogAsync(CrmCodes.OpportunityStage, "Closed Lost", Payload(new
        {
            display = "Closed Lost",
            stage_code = "CLOSED_LOST",
            name = "Closed Lost",
            ordinal = 95,
            default_probability = 0m,
            is_closed = true,
            is_won = false,
            is_active = true
        }), ct, matchField: "stage_code", matchValue: "CLOSED_LOST");

        var products = 0;
        products += await EnsureCatalogAsync(CrmCodes.Product, "Platform Subscription", Payload(new
        {
            display = "Platform Subscription",
            sku = "CRM-SUB",
            name = "Platform Subscription",
            family = "Subscription",
            unit_of_measure = "Month",
            list_price = 1200m,
            currency = CrmCodes.DefaultCurrency,
            is_active = true
        }), ct, matchField: "sku", matchValue: "CRM-SUB");
        products += await EnsureCatalogAsync(CrmCodes.Product, "Implementation Package", Payload(new
        {
            display = "Implementation Package",
            sku = "CRM-IMPL",
            name = "Implementation Package",
            family = "Services",
            unit_of_measure = "Package",
            list_price = 8500m,
            currency = CrmCodes.DefaultCurrency,
            is_active = true
        }), ct, matchField: "sku", matchValue: "CRM-IMPL");

        await EnsureDefaultRolesAsync(ct);
        await EnsureDemoAdministratorUserAsync(ct);

        return new CrmSetupResult(OpportunityStagesEnsured: stages, ProductsEnsured: products);
    }

    private async Task EnsureReferenceRegistersAsync(CancellationToken ct)
    {
        await EnsureLeadFunnelReferenceRegisterAsync(ct);
        await EnsureOpportunitiesReferenceRegisterAsync(ct);
        await EnsureQuotesReferenceRegisterAsync(ct);
        await EnsureActivitiesReferenceRegisterAsync(ct);
    }

    private async Task EnsureLeadFunnelReferenceRegisterAsync(CancellationToken ct)
    {
        var id = await UpsertRegisterAsync(CrmCodes.LeadFunnelRegisterCode, "CRM Lead Funnel", ct);

        await refregManagement.ReplaceFieldsAsync(
            id,
            [
                Field("lead_intake_id", "Lead Intake Id", 1, ColumnType.Guid, false),
                Field("source_document_id", "Source Document Id", 2, ColumnType.Guid, false),
                Field("funnel_step", "Funnel Step", 3, ColumnType.String, false),
                Field("lead_name", "Lead Name", 4, ColumnType.String, false),
                Field("company_name", "Company Name", 5, ColumnType.String, true),
                Field("contact_name", "Contact Name", 6, ColumnType.String, false),
                Field("email", "Email", 7, ColumnType.String, true),
                Field("lead_source", "Lead Source", 8, ColumnType.String, true),
                Field("industry", "Industry", 9, ColumnType.String, true),
                Field("qualification_state", "Qualification State", 10, ColumnType.String, true),
                Field("qualification_score", "Qualification Score", 11, ColumnType.Int32, true),
                Field("converted_account_id", "Converted Account Id", 12, ColumnType.Guid, true),
                Field("converted_contact_id", "Converted Contact Id", 13, ColumnType.Guid, true),
                Field("event_at_utc", "Event At", 14, ColumnType.DateTimeUtc, false),
                Field("updated_at_utc", "Updated At", 15, ColumnType.DateTimeUtc, false)
            ],
            ct);

        await refregManagement.ReplaceDimensionRulesAsync(
            id,
            [
                Dimension(CrmCodes.LeadIntake, 1, isRequired: false),
                Dimension(CrmCodes.LeadQualification, 2, isRequired: false),
                Dimension(CrmCodes.LeadConversion, 3, isRequired: false)
            ],
            ct);

        await refregMaintenance.EnsurePhysicalSchemaByIdAsync(id, ct);
    }

    private async Task EnsureOpportunitiesReferenceRegisterAsync(CancellationToken ct)
    {
        var id = await UpsertRegisterAsync(CrmCodes.OpportunitiesRegisterCode, "CRM Opportunities", ct);

        await refregManagement.ReplaceFieldsAsync(
            id,
            [
                Field("opportunity_id", "Opportunity Id", 1, ColumnType.Guid, false),
                Field("source_document_id", "Source Document Id", 2, ColumnType.Guid, false),
                Field("event_type", "Event Type", 3, ColumnType.String, false),
                Field("event_at_utc", "Event At", 4, ColumnType.DateTimeUtc, false),
                Field("opportunity_name", "Opportunity Name", 5, ColumnType.String, false),
                Field("account_id", "Account Id", 6, ColumnType.Guid, false),
                Field("contact_id", "Contact Id", 7, ColumnType.Guid, false),
                Field("stage_id", "Stage Id", 8, ColumnType.Guid, false),
                Field("amount", "Amount", 9, ColumnType.Decimal, false),
                Field("probability", "Probability", 10, ColumnType.Decimal, false),
                Field("expected_close_date", "Expected Close Date", 11, ColumnType.Date, true),
                Field("status", "Status", 12, ColumnType.String, false),
                Field("loss_reason", "Loss Reason", 13, ColumnType.String, true),
                Field("currency", "Currency", 14, ColumnType.String, false),
                Field("updated_at_utc", "Updated At", 15, ColumnType.DateTimeUtc, false)
            ],
            ct);

        await refregManagement.ReplaceDimensionRulesAsync(
            id,
            [Dimension(CrmCodes.LeadConversion, 1, isRequired: true)],
            ct);

        await refregMaintenance.EnsurePhysicalSchemaByIdAsync(id, ct);
    }

    private async Task EnsureQuotesReferenceRegisterAsync(CancellationToken ct)
    {
        var id = await UpsertRegisterAsync(CrmCodes.QuotesRegisterCode, "CRM Quotes", ct);

        await refregManagement.ReplaceFieldsAsync(
            id,
            [
                Field("quote_id", "Quote Id", 1, ColumnType.Guid, false),
                Field("source_document_id", "Source Document Id", 2, ColumnType.Guid, false),
                Field("opportunity_id", "Opportunity Id", 3, ColumnType.Guid, false),
                Field("account_id", "Account Id", 4, ColumnType.Guid, false),
                Field("contact_id", "Contact Id", 5, ColumnType.Guid, true),
                Field("quote_date", "Quote Date", 6, ColumnType.Date, false),
                Field("valid_until", "Valid Until", 7, ColumnType.Date, false),
                Field("currency", "Currency", 8, ColumnType.String, false),
                Field("quote_status", "Quote Status", 9, ColumnType.String, false),
                Field("amount", "Amount", 10, ColumnType.Decimal, false),
                Field("updated_at_utc", "Updated At", 11, ColumnType.DateTimeUtc, false)
            ],
            ct);

        await refregManagement.ReplaceDimensionRulesAsync(
            id,
            [Dimension(CrmCodes.Quote, 1, isRequired: true)],
            ct);

        await refregMaintenance.EnsurePhysicalSchemaByIdAsync(id, ct);
    }

    private async Task EnsureActivitiesReferenceRegisterAsync(CancellationToken ct)
    {
        var id = await UpsertRegisterAsync(CrmCodes.ActivitiesRegisterCode, "CRM Activities", ct);

        await refregManagement.ReplaceFieldsAsync(
            id,
            [
                Field("activity_id", "Activity Id", 1, ColumnType.Guid, false),
                Field("source_document_id", "Source Document Id", 2, ColumnType.Guid, false),
                Field("activity_date", "Activity Date", 3, ColumnType.Date, false),
                Field("activity_type", "Activity Type", 4, ColumnType.String, false),
                Field("subject", "Subject", 5, ColumnType.String, false),
                Field("lead_intake_id", "Lead Intake Id", 6, ColumnType.Guid, true),
                Field("account_id", "Account Id", 7, ColumnType.Guid, true),
                Field("contact_id", "Contact Id", 8, ColumnType.Guid, true),
                Field("opportunity_id", "Opportunity Id", 9, ColumnType.Guid, true),
                Field("due_at_utc", "Due At", 10, ColumnType.DateTimeUtc, true),
                Field("completed_at_utc", "Completed At", 11, ColumnType.DateTimeUtc, true),
                Field("outcome", "Outcome", 12, ColumnType.String, true),
                Field("updated_at_utc", "Updated At", 13, ColumnType.DateTimeUtc, false)
            ],
            ct);

        await refregManagement.ReplaceDimensionRulesAsync(
            id,
            [Dimension(CrmCodes.ActivityLog, 1, isRequired: true)],
            ct);

        await refregMaintenance.EnsurePhysicalSchemaByIdAsync(id, ct);
    }

    private async Task EnsureDefaultRolesAsync(CancellationToken ct)
    {
        var existingCodes = (await roles.GetRolesAsync(ct))
            .Select(static x => x.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await EnsureRoleAsync(
            existingCodes,
            "crm.administrator",
            "CRM Administrator",
            "Full CRM operations, setup, reports, users, roles, health, and background jobs.",
            CrmAdministratorPermissions(),
            ct);

        await EnsureRoleAsync(
            existingCodes,
            "crm.manager",
            "CRM Manager",
            "Manage CRM customers, pipeline documents, quotes, reports, health, and operational review.",
            CrmManagerPermissions(),
            ct);

        await EnsureRoleAsync(
            existingCodes,
            "crm.sales_rep",
            "CRM Sales Representative",
            "Create and post CRM sales documents, maintain customer lookups, and execute CRM reports.",
            CrmSalesRepresentativePermissions(),
            ct);
    }

    private async Task EnsureDemoAdministratorUserAsync(CancellationToken ct)
    {
        var adminRole = (await roles.GetRolesAsync(ct))
            .Single(role => string.Equals(role.Code, "crm.administrator", StringComparison.OrdinalIgnoreCase));

        var authSubject = Environment.GetEnvironmentVariable("KEYCLOAK_DEMO_ADMIN_ID")
            ?? "6d49204b-867c-4180-a30d-a5e290e13c73";
        var email = Environment.GetEnvironmentVariable("KEYCLOAK_DEMO_ADMIN_EMAIL")
            ?? "alex.carter@demo.ngbplatform.com";
        var firstName = Environment.GetEnvironmentVariable("KEYCLOAK_DEMO_ADMIN_FIRST_NAME") ?? "Alex";
        var lastName = Environment.GetEnvironmentVariable("KEYCLOAK_DEMO_ADMIN_LAST_NAME") ?? "Carter";
        var displayName = $"{firstName.Trim()} {lastName.Trim()}".Trim();

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            var userId = await platformUsers.UpsertAsync(
                authSubject,
                email,
                string.IsNullOrWhiteSpace(displayName) ? email : displayName,
                isActive: true,
                innerCt);

            await userRoles.ReplaceUserRolesAsync(userId, [adminRole.RoleId], assignedByUserId: null, innerCt);
            await userAccessVersions.GetOrCreateAsync(userId, innerCt);
        }, ct);
    }

    private async Task EnsureRoleAsync(
        IReadOnlySet<string> existingCodes,
        string code,
        string name,
        string description,
        IReadOnlyList<PermissionAssignmentDto> permissions,
        CancellationToken ct)
    {
        if (existingCodes.Contains(code))
            return;

        await roles.CreateRoleAsync(new CreateRoleRequestDto(code, name, description, permissions), ct);
    }

    private async Task<Guid> UpsertRegisterAsync(string code, string name, CancellationToken ct)
    {
        return await refregManagement.UpsertAsync(
            code,
            name,
            ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.Independent,
            ct);
    }

    private static ReferenceRegisterFieldDefinition Field(
        string code,
        string name,
        int ordinal,
        ColumnType columnType,
        bool isNullable)
        => new(code, name, ordinal, columnType, isNullable);

    private static ReferenceRegisterDimensionRule Dimension(string code, int ordinal, bool isRequired)
        => new(
            DimensionId: DeterministicGuid.Create($"Dimension|{code}"),
            DimensionCode: code,
            Ordinal: ordinal,
            IsRequired: isRequired);

    private static IReadOnlyList<PermissionAssignmentDto> CrmAdministratorPermissions()
    {
        var permissions = new List<PermissionAssignmentDto>();
        AddSystemPermissions(
            permissions,
            NgbSystemPermissions.UsersView,
            NgbSystemPermissions.UsersManage,
            NgbSystemPermissions.RolesView,
            NgbSystemPermissions.RolesManage,
            NgbSystemPermissions.PermissionsView,
            NgbSystemPermissions.AuditView);
        AddResourceActions(permissions, NgbResourceKinds.Catalog, CrmCatalogTypes, CatalogAdministratorActions);
        AddResourceActions(permissions, NgbResourceKinds.Document, CrmDocumentTypes, DocumentAdministratorActions);
        AddResourceActions(permissions, NgbResourceKinds.Report, CrmReportCodes, ReportAdministratorActions);
        AddPagePermissions(permissions);
        AddExternalPermissions(permissions);
        return DistinctPermissions(permissions);
    }

    private static IReadOnlyList<PermissionAssignmentDto> CrmManagerPermissions()
    {
        var permissions = new List<PermissionAssignmentDto>();
        AddSystemPermissions(
            permissions,
            NgbSystemPermissions.UsersView,
            NgbSystemPermissions.RolesView,
            NgbSystemPermissions.PermissionsView,
            NgbSystemPermissions.AuditView);
        AddResourceActions(permissions, NgbResourceKinds.Catalog, CrmCatalogTypes, CatalogManagerActions);
        AddResourceActions(permissions, NgbResourceKinds.Document, CrmDocumentTypes, DocumentManagerActions);
        AddResourceActions(permissions, NgbResourceKinds.Report, CrmReportCodes, ReportManagerActions);
        AddPagePermissions(permissions);
        AddExternalPermissions(permissions);
        return DistinctPermissions(permissions);
    }

    private static IReadOnlyList<PermissionAssignmentDto> CrmSalesRepresentativePermissions()
    {
        var permissions = new List<PermissionAssignmentDto>();
        AddResourceActions(permissions, NgbResourceKinds.Catalog, CrmCatalogTypes, CatalogSalesActions);
        AddResourceActions(permissions, NgbResourceKinds.Document, CrmDocumentTypes, DocumentSalesActions);
        AddResourceActions(permissions, NgbResourceKinds.Report, CrmReportCodes, ReportSalesActions);
        AddPagePermissions(permissions);
        return DistinctPermissions(permissions);
    }

    private static void AddSystemPermissions(List<PermissionAssignmentDto> permissions, params NgbPermissionKey[] keys)
    {
        foreach (var key in keys)
        {
            permissions.Add(new PermissionAssignmentDto(key.ResourceKind, key.ResourceCode, key.ActionCode));
        }
    }

    private static void AddResourceActions(
        List<PermissionAssignmentDto> permissions,
        string resourceKind,
        IReadOnlyList<string> resources,
        IReadOnlyList<string> actions)
    {
        foreach (var resource in resources)
        {
            foreach (var action in actions)
            {
                permissions.Add(new PermissionAssignmentDto(resourceKind, resource, action));
            }
        }
    }

    private static void AddExternalPermissions(List<PermissionAssignmentDto> permissions)
    {
        permissions.Add(new PermissionAssignmentDto(NgbResourceKinds.External, CrmCodes.Watchdog, NgbPermissionActions.View));
        permissions.Add(new PermissionAssignmentDto(NgbResourceKinds.External, CrmCodes.BackgroundJobs, NgbPermissionActions.View));
    }

    private static void AddPagePermissions(List<PermissionAssignmentDto> permissions)
        => permissions.Add(new PermissionAssignmentDto(NgbResourceKinds.Page, CrmCodes.Dashboard, NgbPermissionActions.View));

    private static IReadOnlyList<PermissionAssignmentDto> DistinctPermissions(
        IEnumerable<PermissionAssignmentDto> permissions)
        => permissions
            .Distinct()
            .OrderBy(static x => x.ResourceKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static x => x.ResourceCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static x => x.ActionCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private async Task<int> EnsureCatalogAsync(
        string catalogType,
        string display,
        RecordPayload payload,
        CancellationToken ct,
        string? matchField = null,
        string? matchValue = null)
    {
        var page = await catalogs.GetPageAsync(
            catalogType,
            new PageRequestDto(
                Offset: 0,
                Limit: 200,
                Search: string.IsNullOrWhiteSpace(matchField) ? display : null),
            ct);

        var matches = page.Items
            .Where(x =>
                string.Equals(x.Display, display, StringComparison.OrdinalIgnoreCase)
                || CatalogPayloadFieldEquals(x, matchField, matchValue))
            .ToArray();

        if (matches.Length > 1)
            throw new InvalidOperationException($"Multiple '{catalogType}' records exist for default '{display}'.");

        if (matches.Length == 0)
        {
            await catalogs.CreateAsync(catalogType, payload, ct);
            return 1;
        }

        await catalogs.UpdateAsync(catalogType, matches[0].Id, payload, ct);
        return 0;
    }

    private static bool CatalogPayloadFieldEquals(
        CatalogItemDto item,
        string? field,
        string? expected)
    {
        if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(expected))
            return false;

        if (item.Payload.Fields is null || !item.Payload.Fields.TryGetValue(field, out var value))
            return false;

        return string.Equals(value.ToString(), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static RecordPayload Payload(object fields)
    {
        var json = JsonSerializer.SerializeToElement(fields);
        var dict = json.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.Clone(),
            StringComparer.OrdinalIgnoreCase);

        return new RecordPayload(dict);
    }

    private static readonly string[] CrmCatalogTypes =
    [
        CrmCodes.Account,
        CrmCodes.Contact,
        CrmCodes.Product,
        CrmCodes.OpportunityStage
    ];

    private static readonly string[] CrmDocumentTypes =
    [
        CrmCodes.LeadIntake,
        CrmCodes.LeadQualification,
        CrmCodes.LeadConversion,
        CrmCodes.OpportunityUpdate,
        CrmCodes.Quote,
        CrmCodes.ActivityLog
    ];

    private static readonly string[] CrmReportCodes =
    [
        CrmCodes.SalesPipelineReport,
        CrmCodes.OpportunityHistoryReport,
        CrmCodes.LeadConversionFunnelReport,
        CrmCodes.ActivitySummaryReport,
        CrmCodes.QuoteRegisterReport
    ];

    private static readonly string[] CatalogAdministratorActions =
    [
        NgbPermissionActions.View,
        NgbPermissionActions.Lookup,
        NgbPermissionActions.Create,
        NgbPermissionActions.Edit,
        NgbPermissionActions.MarkForDeletion,
        NgbPermissionActions.UnmarkForDeletion,
        NgbPermissionActions.ViewAudit
    ];

    private static readonly string[] CatalogManagerActions =
    [
        NgbPermissionActions.View,
        NgbPermissionActions.Lookup,
        NgbPermissionActions.Create,
        NgbPermissionActions.Edit,
        NgbPermissionActions.ViewAudit
    ];

    private static readonly string[] CatalogSalesActions =
    [
        NgbPermissionActions.View,
        NgbPermissionActions.Lookup
    ];

    private static readonly string[] DocumentAdministratorActions =
    [
        NgbPermissionActions.View,
        NgbPermissionActions.Lookup,
        NgbPermissionActions.Create,
        NgbPermissionActions.EditDraft,
        NgbPermissionActions.DeleteDraft,
        NgbPermissionActions.MarkForDeletion,
        NgbPermissionActions.UnmarkForDeletion,
        NgbPermissionActions.Post,
        NgbPermissionActions.Unpost,
        NgbPermissionActions.Repost,
        NgbPermissionActions.ViewEffects,
        NgbPermissionActions.ViewFlow,
        NgbPermissionActions.ViewAudit,
        NgbPermissionActions.Print
    ];

    private static readonly string[] DocumentManagerActions =
    [
        NgbPermissionActions.View,
        NgbPermissionActions.Lookup,
        NgbPermissionActions.Create,
        NgbPermissionActions.EditDraft,
        NgbPermissionActions.DeleteDraft,
        NgbPermissionActions.MarkForDeletion,
        NgbPermissionActions.UnmarkForDeletion,
        NgbPermissionActions.Post,
        NgbPermissionActions.Repost,
        NgbPermissionActions.ViewEffects,
        NgbPermissionActions.ViewFlow,
        NgbPermissionActions.ViewAudit,
        NgbPermissionActions.Print
    ];

    private static readonly string[] DocumentSalesActions =
    [
        NgbPermissionActions.View,
        NgbPermissionActions.Lookup,
        NgbPermissionActions.Create,
        NgbPermissionActions.EditDraft,
        NgbPermissionActions.DeleteDraft,
        NgbPermissionActions.Post,
        NgbPermissionActions.ViewEffects,
        NgbPermissionActions.ViewFlow,
        NgbPermissionActions.Print
    ];

    private static readonly string[] ReportAdministratorActions =
    [
        NgbPermissionActions.View,
        NgbPermissionActions.Execute,
        NgbPermissionActions.Export,
        NgbPermissionActions.SavePrivateVariant,
        NgbPermissionActions.ManageSharedVariants,
        NgbPermissionActions.DeleteVariant
    ];

    private static readonly string[] ReportManagerActions =
    [
        NgbPermissionActions.View,
        NgbPermissionActions.Execute,
        NgbPermissionActions.Export,
        NgbPermissionActions.SavePrivateVariant,
        NgbPermissionActions.ManageSharedVariants
    ];

    private static readonly string[] ReportSalesActions =
    [
        NgbPermissionActions.View,
        NgbPermissionActions.Execute,
        NgbPermissionActions.Export,
        NgbPermissionActions.SavePrivateVariant
    ];
}
