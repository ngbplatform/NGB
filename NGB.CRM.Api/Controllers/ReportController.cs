using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Api.Controllers;
using NGB.Application.Abstractions.Services;
using NGB.Runtime.Security;

namespace NGB.CRM.Api.Controllers;

[Authorize]
[ApiController]
public sealed class ReportController(
    IReportDefinitionProvider definitions,
    IReportEngine engine,
    IReportVariantService variants,
    IReportDownloadService downloads,
    INgbAccessChecker access,
    NgbSecurityCache cache)
    : ReportControllerBase(definitions, engine, variants, downloads, access, cache);
