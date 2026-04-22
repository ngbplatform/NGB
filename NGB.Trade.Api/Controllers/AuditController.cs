using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Api.Controllers;
using NGB.Application.Abstractions.Services;

namespace NGB.Trade.Api.Controllers;

[Authorize]
[ApiController]
public sealed class AuditController(IAuditLogQueryService service) : AuditControllerBase(service);
