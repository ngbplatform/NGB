using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Features;
using NGB.Core.Features;

namespace NGB.Api.Features;

/// <summary>Requires at least one enabled feature before MVC constructs the controller.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class NgbFeatureAttribute(params string[] features) : Attribute, IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var service = context.HttpContext.RequestServices.GetService<INgbFeatureService>();
        if (service is not null)
        {
            foreach (var feature in features)
            {
                if (await service.IsEnabledAsync(feature, context.HttpContext.RequestAborted))
                {
                    await next();
                    return;
                }
            }
        }

        throw new NgbFeatureDisabledException(string.Join(",", features));
    }
}
