using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Api.Controllers;
using NGB.Application.Abstractions.Services;

namespace NGB.Trade.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/catalogs/{catalogType}")]
public sealed class CatalogController(ICatalogService service) : CatalogControllerBase(service);
