using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Api.Controllers;
using NGB.Application.Abstractions.Services;
using NGB.Runtime.Security;

namespace NGB.Trade.Api.Controllers;

[Authorize]
[ApiController]
public sealed class ReportController(
    IAuthorizedReportReadService reads,
    IReportVariantService variants,
    IReportDownloadService downloads,
    INgbAccessChecker access)
    : ReportControllerBase(reads, variants, downloads, access);
