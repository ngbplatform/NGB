using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NGB.Runtime.Security;

namespace NGB.AiAssistant.AspNetCore;

internal static class AiAssistantHttpEndpoint
{
    private const int ReadBufferBytes = 8192;
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ErrorWriteTimeout = TimeSpan.FromSeconds(5);
    private static readonly Meter Meter = new("NGB.AiAssistant");
    private static readonly Counter<long> Requests = Meter.CreateCounter<long>("ngb.ai_assistant.requests");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("ngb.ai_assistant.duration", "s");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static async Task<IResult> SettingsAsync(HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store";
        var snapshot = await http.RequestServices.GetRequiredService<INgbAccessChecker>().GetSnapshotAsync(http.RequestAborted);
        if (!snapshot.IsAuthenticated || !snapshot.IsActive)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var options = http.RequestServices.GetRequiredService<IOptions<AiAssistantOptions>>().Value;
        var model = http.RequestServices.GetService<AiAssistantModelInfo>();

        return Results.Ok(new
        {
            options.MaxMessages,
            options.MaxMessageCharacters,
            options.MaxInputBytes,
            options.MaxOutputCharacters,
            model?.Provider,
            model?.Model
        });
    }

    internal static async Task HandleAsync(HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.XContentTypeOptions = "nosniff";

        var services = http.RequestServices;
        var snapshot = await services.GetRequiredService<INgbAccessChecker>().GetSnapshotAsync(http.RequestAborted);

        if (!snapshot.IsAuthenticated || !snapshot.IsActive)
        {
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var subject = http.User.FindFirstValue("sub") ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var admission = services.GetRequiredService<AiAssistantAdmission>();

        if (!admission.TryEnter(subject))
        {
            http.Response.Headers.RetryAfter = "5";
            http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }

        var limits = services.GetRequiredService<IOptions<AiAssistantOptions>>().Value;
        var started = Stopwatch.GetTimestamp();
        var outcome = "success";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
        deadline.CancelAfter(TimeSpan.FromSeconds(limits.RequestTimeoutSeconds));
        using var writes = new SemaphoreSlim(1);
        using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        Task? heartbeat = null;

        async ValueTask Publish(AiAssistantEvent item, CancellationToken ct)
        {
            await writes.WaitAsync(ct);
            try
            {
                await http.Response.WriteAsync("data: " + JsonSerializer.Serialize(item, Json) + "\n\n", ct);
                await http.Response.Body.FlushAsync(ct);
            }
            finally
            {
                writes.Release();
            }
        }

        try
        {
            if (!http.Request.HasJsonContentType())
            {
                http.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                return;
            }

            var request = await ReadRequestAsync(http.Request, limits.MaxInputBytes, deadline.Token);
            var runner = services.GetRequiredService<AiAssistantRunner>();
            runner.Validate(request);
            http.Response.ContentType = "text/event-stream";
            http.Response.Headers["X-Accel-Buffering"] = "no";
            http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            await http.Response.StartAsync(deadline.Token);
            heartbeat = SendHeartbeatsAsync(Publish, deadline, heartbeatStop.Token);
            await runner.RunAsync(request, Publish, deadline.Token);
        }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
        {
            outcome = "cancelled";
        }
        catch (Exception exception)
        {
            outcome = "error";
            var error = exception switch
            {
                AiAssistantException known => new AiAssistantEvent("error", known.Message, known.Code),
                OperationCanceledException when deadline.IsCancellationRequested => new("error", "The request timed out. Please narrow the question or try again.", "timeout"),
                _ => new("error", "The assistant could not complete the request. Please try again.", "unavailable")
            };

            if (error.Code == "unavailable")
                services.GetRequiredService<ILoggerFactory>().CreateLogger("NGB.AiAssistant")
                    .LogWarning("AI Assistant request failed with {ExceptionType}; trace {TraceId}.", exception.GetType().Name, http.TraceIdentifier);

            if (!http.RequestAborted.IsCancellationRequested)
            {
                using var errorWrite = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
                errorWrite.CancelAfter(ErrorWriteTimeout);
                try
                {
                    if (http.Response.HasStarted)
                    {
                        await Publish(error, errorWrite.Token);
                    }
                    else
                    {
                        http.Response.StatusCode = error.Code switch
                        {
                            "invalid_input" => StatusCodes.Status400BadRequest,
                            "input_limit" => StatusCodes.Status413PayloadTooLarge,
                            "timeout" => StatusCodes.Status504GatewayTimeout,
                            _ => StatusCodes.Status503ServiceUnavailable
                        };

                        await http.Response.WriteAsJsonAsync(error, cancellationToken: errorWrite.Token);
                    }
                }
                catch (Exception writeError) when (writeError is IOException || writeError is OperationCanceledException && errorWrite.IsCancellationRequested)
                {
                    http.Abort();
                }
            }
        }
        finally
        {
            try
            {
                await heartbeatStop.CancelAsync();

                if (heartbeat is not null)
                    await heartbeat;
            }
            catch (OperationCanceledException) when (heartbeatStop.IsCancellationRequested)
            {
            }
            catch (IOException)
            {
                outcome = "disconnected";
            }
            finally
            {
                admission.Exit(subject);
                Requests.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
                Duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds);
            }
        }
    }

    private static async Task<AiAssistantRequest> ReadRequestAsync(
        HttpRequest request,
        int maximumBytes,
        CancellationToken ct)
    {
        if (request.ContentLength > maximumBytes)
            throw InputLimit();

        using var body = new MemoryStream();
        var buffer = new byte[Math.Min(ReadBufferBytes, maximumBytes)];
        while (true)
        {
            var count = await request.Body.ReadAsync(buffer, ct);
            if (count == 0)
                break;

            if (body.Length + count > maximumBytes)
                throw InputLimit();

            body.Write(buffer, 0, count);
        }

        try
        {
            return JsonSerializer.Deserialize<AiAssistantRequest>(body.GetBuffer().AsSpan(0, (int)body.Length), Json)
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new AiAssistantException("invalid_input", "The assistant request is invalid.");
        }
    }

    private static async Task SendHeartbeatsAsync(
        Func<AiAssistantEvent, CancellationToken, ValueTask> publish,
        CancellationTokenSource request,
        CancellationToken ct)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await publish(new("heartbeat"), ct);
            }
        }
        catch (IOException)
        {
            await request.CancelAsync();
            throw;
        }
    }

    private static AiAssistantException InputLimit()
        => new("input_limit", "The conversation is too large. Start a new chat or shorten the question.");
}
