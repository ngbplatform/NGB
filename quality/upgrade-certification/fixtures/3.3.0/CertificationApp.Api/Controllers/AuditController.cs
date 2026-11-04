using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Api.Controllers;
using NGB.Application.Abstractions.Services;

namespace CertificationApp.Api.Controllers;

[Authorize]
[ApiController]
public sealed class AuditController(IAuditLogQueryService service) : AuditControllerBase(service);
