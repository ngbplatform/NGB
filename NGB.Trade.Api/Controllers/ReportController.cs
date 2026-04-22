using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Api.Controllers;
using NGB.Application.Abstractions.Services;

namespace NGB.Trade.Api.Controllers;

[Authorize]
[ApiController]
public sealed class ReportController(
    IReportDefinitionProvider definitions,
    IReportEngine engine,
    IReportVariantService variants,
    IReportExportService exports)
    : ReportControllerBase(definitions, engine, variants, exports);
