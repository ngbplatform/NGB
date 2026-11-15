using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Api.Controllers;
using NGB.Application.Abstractions.Services;
using NGB.Runtime.Documents;

namespace NgbApplication.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/documents/{documentType}")]
public sealed class DocumentController(
    PermissionAwareDocumentService service,
    IDocumentActionQueryService queries,
    IDocumentActionDispatcher dispatcher)
    : DocumentControllerBase(service, queries, dispatcher);
