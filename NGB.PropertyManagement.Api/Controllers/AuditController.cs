using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Api.Controllers;
using NGB.Runtime.AuditLog;

namespace NGB.PropertyManagement.Api.Controllers;

[Authorize]
[ApiController]
public sealed class AuditController(PermissionAwareAuditLogQueryService service) : AuditControllerBase(service);
