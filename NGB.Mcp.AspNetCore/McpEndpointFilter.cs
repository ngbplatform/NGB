using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NGB.Runtime.Security;
using NGB.Tools.Http;

namespace NGB.Mcp.AspNetCore;

internal sealed class McpEndpointFilter(
    IOptions<NgbMcpOptions> options,
    McpRequestBudget budget,
    McpEndpointAddress endpoint)
    : IEndpointFilter
{
    // Buffer in small chunks; a larger configured budget must not allocate that entire budget per request.
    private const int BodyReadBufferBytes = 16 * 1024;

    // The JSON-RPC root and params objects wrap the tool's arguments object.
    private const int JsonRpcEnvelopeDepth = 2;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var settings = options.Value;
        var resource = endpoint.Resource;
        http.Response.Headers.CacheControl = "no-store";

        if ((!http.Request.IsHttps && !(settings.AllowLocalhostHttp && HttpEndpointUri.IsLocalhost(resource)))
            || !string.Equals(http.Request.Host.Value, resource.Authority, StringComparison.OrdinalIgnoreCase)
            || !string.Equals((http.Request.PathBase + http.Request.Path).Value, resource.AbsolutePath, StringComparison.Ordinal))
        {
            return Results.StatusCode(StatusCodes.Status400BadRequest);
        }

        var origin = http.Request.Headers.Origin.ToString();
        if (origin.Length > 0 && !string.Equals(origin, resource.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal)
            && !settings.AllowedOrigins.Contains(origin, StringComparer.Ordinal))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        using var rate = budget.Rate.AttemptAcquire();
        using var concurrency = budget.Concurrency.AttemptAcquire();
        if (!rate.IsAcquired || !concurrency.IsAcquired)
        {
            http.Response.Headers.RetryAfter = "3";
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        var original = http.RequestAborted;
        var originalBody = http.Request.Body;
        MemoryStream? bufferedBody = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(original);
        deadline.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        http.RequestAborted = deadline.Token;
        try
        {
            var snapshot = await http.RequestServices.GetRequiredService<INgbAccessChecker>().GetSnapshotAsync(deadline.Token);
            if (!snapshot.IsAuthenticated || !snapshot.IsActive)
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            if (HttpMethods.IsPost(http.Request.Method))
            {
                if (http.Request.ContentLength > settings.MaxRequestBytes)
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

                try
                {
                    // Bound actual bytes even when another middleware has already buffered
                    // a chunked request. EnableBuffering cannot reduce an existing buffer limit.
                    bufferedBody = new MemoryStream();
                    var buffer = new byte[Math.Min(settings.MaxRequestBytes, BodyReadBufferBytes)];
                    while (true)
                    {
                        var remaining = (long)settings.MaxRequestBytes - bufferedBody.Length;
                        var readLength = (int)Math.Min(buffer.Length, remaining + 1);
                        var length = await originalBody.ReadAsync(buffer.AsMemory(0, readLength), deadline.Token);
                        if (length == 0)
                            break;

                        if (length > remaining)
                            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

                        bufferedBody.Write(buffer, 0, length);
                    }

                    using var json = JsonDocument.Parse(bufferedBody.GetBuffer().AsMemory(0, (int)bufferedBody.Length),
                        new JsonDocumentOptions { MaxDepth = McpReadOptions.MaximumSupportedJsonDepth + JsonRpcEnvelopeDepth });
                    ValidateProperties(json.RootElement);
                    bufferedBody.Position = 0;
                    http.Request.Body = bufferedBody;
                }
                catch (JsonException)
                {
                    return Results.StatusCode(StatusCodes.Status400BadRequest);
                }
                catch (IOException)
                {
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                }
            }

            return await next(context);
        }
        catch (OperationCanceledException) when (!original.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            if (http.Response.HasStarted)
            {
                http.Abort();
                return null;
            }

            return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
        }
        finally
        {
            http.RequestAborted = original;
            http.Request.Body = originalBody;
            bufferedBody?.Dispose();
        }
    }

    private static void ValidateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new JsonException("Duplicate JSON properties are not supported.");

                ValidateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                ValidateProperties(item);
            }
        }
    }
}
