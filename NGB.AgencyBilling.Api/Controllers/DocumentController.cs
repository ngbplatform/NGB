using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Api.Controllers;
using NGB.Runtime.Documents;

namespace NGB.AgencyBilling.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/documents/{documentType}")]
public sealed class DocumentController(PermissionAwareDocumentService service) : DocumentControllerBase(service);
