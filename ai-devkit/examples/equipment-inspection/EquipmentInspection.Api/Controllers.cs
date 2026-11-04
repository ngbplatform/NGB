using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Api.Controllers;
using NGB.Application.Abstractions.Services;
using NGB.Runtime.Admin;
using NGB.Runtime.Catalogs;
using NGB.Runtime.Documents;
using NGB.Runtime.Security;

namespace EquipmentInspection.Api;

[Authorize]
[ApiController]
[Route("api/catalogs/{catalogType}")]
public sealed class EquipmentCatalogController(PermissionAwareCatalogService service) : CatalogControllerBase(service);

[Authorize]
[ApiController]
[Route("api/documents/{documentType}")]
public sealed class InspectionDocumentController(
    PermissionAwareDocumentService service,
    IDocumentActionQueryService queries,
    IDocumentActionDispatcher dispatcher) : DocumentControllerBase(service, queries, dispatcher);

[Authorize]
[ApiController]
public sealed class SecurityController(
    ICurrentAccessService currentAccess,
    PermissionDefinitionRegistry permissionDefinitions,
    IUserAccessManagementService users,
    IRoleManagementService roles,
    IEffectiveAccessService effectiveAccess,
    INgbAccessChecker access)
    : SecurityControllerBase(currentAccess, permissionDefinitions, users, roles, effectiveAccess, access);

[Authorize]
[ApiController]
public sealed class AuditController(IAuditLogQueryService service) : AuditControllerBase(service);

[Authorize]
[ApiController]
public sealed class WorkCenterController(IWorkCenterQueryService service) : WorkCenterControllerBase(service);

[Authorize]
[ApiController]
public sealed class ReportController(
    IAuthorizedReportReadService reads,
    IReportVariantService variants,
    IReportDownloadService downloads,
    INgbAccessChecker access) : ReportControllerBase(reads, variants, downloads, access);

[Authorize]
[ApiController]
public sealed class AdminController(PermissionAwareAdminService service) : AdminControllerBase(service);
