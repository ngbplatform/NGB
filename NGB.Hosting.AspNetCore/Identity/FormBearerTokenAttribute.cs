using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace NGB.Hosting.AspNetCore.Identity;

/// <summary>Opts a native browser POST endpoint into RFC 6750 section 2.2 bearer transport.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class FormBearerTokenAttribute : Attribute
{
    public const int MaximumBodyBytes = 256 * 1024;

    public static async Task ReadTokenAsync(MessageReceivedContext context)
    {
        var request = context.Request;
        if (context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<FormBearerTokenAttribute>() is null)
            return;

        // Header authentication retains its normal behavior; never replace it with a form token.
        if (request.Headers.ContainsKey("Authorization")) return;
        if (!HttpMethods.IsPost(request.Method)
            || !string.Equals(request.ContentType?.Split(';')[0].Trim(), "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)
            || request.ContentLength > MaximumBodyBytes)
        {
            context.Fail("Invalid form authentication request.");
            return;
        }

        var size = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (size is { IsReadOnly: false })
            size.MaxRequestBodySize = MaximumBodyBytes;

        try
        {
            var form = await request.ReadFormAsync(new FormOptions
            {
                ValueCountLimit = 2,
                KeyLengthLimit = 32,
                ValueLengthLimit = MaximumBodyBytes / 2,
                BufferBody = false
            }, context.HttpContext.RequestAborted);
            var token = form["access_token"];

            if (token.Count != 1 || string.IsNullOrWhiteSpace(token[0]))
                context.Fail("A single access token is required.");
            else
                context.Token = token[0];
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException)
        {
            context.Fail("Invalid form authentication request.");
        }
    }
}
