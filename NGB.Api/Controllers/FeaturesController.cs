using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Application.Abstractions.Features;
using NGB.Contracts.Features;

namespace NGB.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/features")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FeaturesController(INgbFeatureService? features = null) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<FeatureStateDto>> Get(CancellationToken ct)
        => features?.GetAllAsync(ct) ?? Task.FromResult<IReadOnlyList<FeatureStateDto>>([]);
}
