using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Api.Controllers;
using NGB.Runtime.Catalogs;

namespace NGB.CRM.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/catalogs/{catalogType}")]
public sealed class CatalogController(PermissionAwareCatalogService service) : CatalogControllerBase(service);
