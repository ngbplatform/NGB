using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Api.Controllers;
using NGB.Application.Abstractions.Services;
using NGB.Runtime.Security;

namespace NGB.AgencyBilling.Api.Controllers;

[Authorize]
[ApiController]
public sealed class ReportController(
    IReportDefinitionProvider definitions,
    IReportEngine engine,
    IReportVariantService variants,
    IReportExportService exports,
    INgbAccessChecker access,
    NgbSecurityCache cache)
    : ReportControllerBase(definitions, engine, variants, exports, access, cache);
