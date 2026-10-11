using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Api.Controllers;
using NGB.Application.Abstractions.Services;

namespace NgbApplication.Api.Controllers;

[Authorize]
[ApiController]
public sealed class WorkCenterController(IWorkCenterQueryService workCenter) : WorkCenterControllerBase(workCenter);
