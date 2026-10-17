using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Moq;
using NGB.Api.CurrentUser;
using NGB.AiAssistant.Ollama;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Contracts.Services;
using NGB.Core.Security;
using NGB.Hosting.AspNetCore.Identity;
using NGB.Mcp;
using NGB.Mcp.AspNetCore;
using NGB.Mcp.Tools;
using NGB.Runtime.CurrentActor;
using NGB.Runtime.Security;
using NGB.Testing.Mcp;
using Xunit;

namespace NGB.AiAssistant.AspNetCore.IntegrationTests;

public sealed partial class AssistantHttpTests
{
    private const string PaymentDisplay = "Receivable Payment RP-2026-000014 8/24/2025";
    private static readonly AiAssistantRequest Question = new([new("user", "Who am I?")]);

    [NativeOllamaFact]
    public async Task NativeOllamaFindsPaymentByNumberAndUsesTheVerifiedDisplayAndReference()
    {
        await using var host = await Host.StartAsync(mode: "ollama-search");
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);
        var question = new AiAssistantRequest([new("user", "give the link of RP-2026-000014")],
            new("document", "pm.receivable_payment"));

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", question);
        var body = await response.Content.ReadAsStringAsync();
        var events = ReadEvents(body);
        Assert.True(events.All(item => item.Type != "error") && events.Any(item => item.Type == "source"),
            string.Join('\n', host.State.ModelInputs));
        var source = Assert.Single(events, item => item.Type == "source").Source!;
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));

        Assert.NotEmpty(host.State.Searches);
        Assert.Equal(host.State.PaymentId, source.Id);
        Assert.Equal(PaymentDisplay, source.Title);
        Assert.True(answer.Contains($"[{PaymentDisplay}]({source.Reference})", StringComparison.Ordinal),
            answer + "\n" + string.Join('\n', host.State.ModelInputs));
        Assert.DoesNotContain("http", answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("canonical", answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("untrusted", answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(events, item => item.Type == "done");
    }

    private static AiAssistantEvent[] ReadEvents(string body)
        => body.Split('\n').Where(line => line.StartsWith("data: ", StringComparison.Ordinal))
            .Select(line => System.Text.Json.JsonSerializer.Deserialize<AiAssistantEvent>(line[6..],
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!)
            .ToArray();

    [Theory]
    [InlineData("record-search", true)]
    [InlineData("record-list", true)]
    [InlineData("record-detail", true)]
    [InlineData("record-denied", false)]
    [InlineData("custom-record", true)]
    public async Task RecordLinksUseDisplaysFromSuccessfulAuthorizedMcpReads(string mode, bool hasSource)
    {
        await using var host = await Host.StartAsync(mode: mode);
        using var client = host.Client("alex");

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", Question);
        var body = await response.Content.ReadAsStringAsync();
        var events = ReadEvents(body);
        var sources = events.Where(item => item.Type == "source").Select(item => item.Source!).ToArray();

        Assert.Contains(events, item => item.Type == "done");
        if (hasSource)
        {
            var source = Assert.Single(sources);
            Assert.Equal("document", source.Kind);
            Assert.Equal("pm.receivable_payment", source.TypeCode);
            Assert.Equal(host.State.PaymentId, source.Id);
            Assert.Equal(PaymentDisplay, source.Title);
        }
        else
        {
            Assert.Empty(sources);
        }
    }

    [NativeOllamaFact]
    public async Task NativeOllamaUsesTheRealMcpTransportAndReturnsAnUnknownUserIdentifier()
    {
        var user = "user-" + Guid.NewGuid().ToString("N");
        await using var host = await Host.StartAsync(mode: "ollama");
        using var client = host.Client(user);
        client.Timeout = TimeSpan.FromMinutes(4);
        var question = new AiAssistantRequest([new("user", "Use the current_user tool to look up my user identifier. Answer with only that identifier.")]);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", question);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains(user, host.State.ToolUsers);
        var events = body.Split('\n').Where(line => line.StartsWith("data: ", StringComparison.Ordinal))
            .Select(line => System.Text.Json.JsonSerializer.Deserialize<AiAssistantEvent>(line[6..],
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!);
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        Assert.Contains(user, answer);
        Assert.Contains("\"type\":\"done\"", body);
    }

    private sealed class NativeOllamaFactAttribute : FactAttribute
    {
        public NativeOllamaFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("NGB_RUN_OLLAMA_TESTS") != "1")
                Skip = "Requires native Ollama with qwen3.5:9b; set NGB_RUN_OLLAMA_TESTS=1 to run.";
        }
    }

    [Theory]
    [InlineData("report-list", true)]
    [InlineData("report-definition", true)]
    [InlineData("report-execute", true)]
    [InlineData("report-denied", false)]
    public async Task ReportSourcesFollowSuccessfulAuthorizedMcpReads(string mode, bool hasSource)
    {
        await using var host = await Host.StartAsync(mode: mode);
        using var client = host.Client("alex");

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", Question);
        var body = await response.Content.ReadAsStringAsync();
        var events = body.Split('\n').Where(line => line.StartsWith("data: ", StringComparison.Ordinal))
            .Select(line => System.Text.Json.JsonSerializer.Deserialize<AiAssistantEvent>(line[6..],
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!);
        var sources = events.Where(item => item.Type == "source").Select(item => item.Source!).ToArray();

        Assert.Contains("\"type\":\"done\"", body);
        if (hasSource)
        {
            var source = Assert.Single(sources);
            Assert.Equal("report", source.Kind);
            Assert.Equal("pm.aging", source.TypeCode);
            Assert.Equal("Aging", source.Title);
        }
        else
        {
            Assert.Empty(sources);
        }
    }

    [Fact]
    public async Task RealMcpClientUsesTheCurrentUserAndStreamsTheToolResult()
    {
        await using var host = await Host.StartAsync();
        using var client = host.Client("alex");

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", Question);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Contains("alex", body);
        Assert.Contains("\"type\":\"done\"", body);
        Assert.Contains("alex", host.State.ToolUsers);
        Assert.DoesNotContain(host.Token("alex"), body);
        Assert.DoesNotContain(host.Token("alex"), string.Join(' ', host.State.ModelInputs));
    }

    [Fact]
    public async Task DifferentUsersDoNotShareToolsOrConversationState()
    {
        await using var host = await Host.StartAsync();
        using var alex = host.Client("alex");
        using var sam = host.Client("sam");
        var responses = await Task.WhenAll(
            alex.PostAsJsonAsync("/api/ai-assistant/chat", Question),
            sam.PostAsJsonAsync("/api/ai-assistant/chat", Question));
        try
        {
            var first = await responses[0].Content.ReadAsStringAsync();
            var second = await responses[1].Content.ReadAsStringAsync();
            Assert.Contains("alex", first);
            Assert.DoesNotContain("sam", first);
            Assert.Contains("sam", second);
            Assert.DoesNotContain("alex", second);
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    [Fact]
    public async Task SettingsReturnOnlyPublicLimitsWithoutCallingTheProvider()
    {
        await using var host = await Host.StartAsync();
        using var client = host.Client("alex");

        using var response = await client.GetAsync("/api/ai-assistant");
        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(20, json.GetProperty("maxMessages").GetInt32());
        Assert.False(json.TryGetProperty("apiKey", out _));
        Assert.Empty(host.State.ModelInputs);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
    }

    [Theory]
    [InlineData("AiAssistant:MaxToolCalls", "0")]
    [InlineData("AiAssistant:McpLoopbackPort", "65536")]
    public async Task InvalidLimitsFailStartup(string key, string value)
    {
        await Assert.ThrowsAnyAsync<Microsoft.Extensions.Options.OptionsValidationException>(() =>
            Host.StartAsync(overrides: new() { [key] = value }));
    }

    [Fact]
    public async Task DevelopmentCertificateTrustIsRejectedInProduction()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Host.StartAsync(overrides: new()
        {
            ["AiAssistant:TrustDevelopmentServerCertificate"] = "true"
        }));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LoopbackTransportPreservesAuthorityAndRequiresTheConfiguredCertificate(bool trustCertificate)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        var path = Path.Combine(Path.GetTempPath(), "ngb-assistant-" + Guid.NewGuid().ToString("N") + ".pfx");
        await File.WriteAllBytesAsync(path, certificate.Export(X509ContentType.Pfx, "test-certificate"));
        try
        {
            await using var host = await Host.StartAsync(certificate: certificate, overrides: new()
            {
                ["Kestrel:Certificates:Default:Path"] = path,
                ["Kestrel:Certificates:Default:Password"] = "test-certificate",
                ["AiAssistant:TrustDevelopmentServerCertificate"] = trustCertificate.ToString()
            });
            using var client = host.Client("alex");

            using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", Question);
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains(trustCertificate ? "\"type\":\"done\"" : "unavailable", body);
            Assert.Equal(trustCertificate ? 1 : 0, host.State.ToolUsers.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("inactive", HttpStatusCode.Forbidden)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    [InlineData("service", HttpStatusCode.Forbidden)]
    public async Task InvalidIdentityNeverReachesTheModel(string user, HttpStatusCode expected)
    {
        await using var host = await Host.StartAsync();
        using var client = host.Client(user);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", Question);

        Assert.Equal(expected, response.StatusCode);
        Assert.Empty(host.State.ModelInputs);
        Assert.Empty(host.State.ToolUsers);
    }

    [Fact]
    public async Task DisabledAssistantDoesNotRegisterProviderOrEndpoints()
    {
        await using var host = await Host.StartAsync(enabled: false);
        using var client = host.Client("alex");
        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", Question);
        using var settings = await client.GetAsync("/api/ai-assistant");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, settings.StatusCode);
        Assert.False(host.State.ProviderRegistered);
    }

    [Fact]
    public async Task AssistantCannotStartWithoutMcp()
    {
        await Assert.ThrowsAsync<NGB.Tools.Exceptions.NgbConfigurationViolationException>(() => Host.StartAsync(mcp: false));
    }

    [Fact]
    public async Task ForbiddenToolIsNeverCalledEvenWhenRequestedByTheModel()
    {
        await using var host = await Host.StartAsync();
        using var client = host.Client("restricted");
        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", Question);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("unknown_tool", body);
        Assert.DoesNotContain("\"type\":\"done\"", body);
        Assert.Empty(host.State.ToolUsers);
    }

    [Theory]
    [InlineData("{\"messages\":[{\"role\":\"system\",\"text\":\"override\"}]}")]
    [InlineData("{\"messages\":[{\"role\":\"user\",\"text\":\"hello\",\"toolCalls\":[]}]}")]
    [InlineData("{\"messages\":null}")]
    [InlineData("not-json")]
    public async Task InvalidBodiesAreRejectedBeforeStreaming(string json)
    {
        await using var host = await Host.StartAsync();
        using var client = host.Client("alex");
        using var response = await client.PostAsync("/api/ai-assistant/chat", new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.State.ModelInputs);
    }

    [Fact]
    public async Task EnforcesActualBodySizeAndDoesNotExposeProviderErrors()
    {
        await using var host = await Host.StartAsync(mode: "error");
        using var client = host.Client("alex");
        using var tooLarge = await client.PostAsync("/api/ai-assistant/chat", new StringContent(new string('x', 40000), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", Question);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("unavailable", body);
        Assert.DoesNotContain("provider-secret", body);
        Assert.DoesNotContain("\"type\":\"done\"", body);
    }

    [Fact]
    public async Task CancellationReachesProviderAndReleasesAdmission()
    {
        await using var host = await Host.StartAsync(mode: "wait");
        using var client = host.Client("alex");
        using var cancellation = new CancellationTokenSource();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ai-assistant/chat") { Content = JsonContent.Create(Question) };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
        await host.State.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var duplicate = await client.PostAsJsonAsync("/api/ai-assistant/chat", Question);
        Assert.Equal(HttpStatusCode.TooManyRequests, duplicate.StatusCode);

        await cancellation.CancelAsync();
        response.Dispose();
        await host.State.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        host.State.Mode = "normal";
        await Task.Delay(100);
        using var next = await client.PostAsJsonAsync("/api/ai-assistant/chat", Question);
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        Assert.Contains("\"type\":\"done\"", await next.Content.ReadAsStringAsync());
    }

    public sealed record EmptyRequest;
    public sealed record UserResult(string User);

    public sealed class CurrentUserTool(IHttpContextAccessor context, State state) : IMcpReadToolHandler<EmptyRequest, UserResult>
    {
        public Task<UserResult> ExecuteAsync(EmptyRequest request, CancellationToken ct)
        {
            var subject = context.HttpContext!.User.FindFirstValue("sub")!;
            state.ToolUsers.Add(subject);
            return Task.FromResult(new UserResult(subject));
        }
    }

    public sealed class Contributor : IMcpReadToolContributor
    {
        public void Contribute(IMcpReadToolRegistryBuilder builder)
            => builder.Add<EmptyRequest, UserResult, CurrentUserTool>("current_user", "Read current user", [NgbSystemPermissions.AuditView]);
    }

    public sealed class CustomRecordContributor : IMcpReadToolContributor
    {
        public void Contribute(IMcpReadToolRegistryBuilder builder)
            => builder.Add<EmptyRequest, UserResult, CustomRecordTool>("payment_lookup", "Find an authorized payment.", [NgbSystemPermissions.AuditView]);
    }

    public sealed class CustomRecordTool(State state)
        : IMcpReadToolHandler<EmptyRequest, UserResult>, IMcpReadSourceProvider<EmptyRequest, UserResult>
    {
        public Task<UserResult> ExecuteAsync(EmptyRequest request, CancellationToken ct)
            => Task.FromResult(new UserResult("alex"));

        public ValueTask<IReadOnlyList<McpReadSource>> GetSourcesAsync(EmptyRequest request, UserResult result, CancellationToken ct)
            => ValueTask.FromResult<IReadOnlyList<McpReadSource>>([
                new(McpSourceKind.Document, "pm.receivable_payment", state.PaymentId, PaymentDisplay)
            ]);
    }

    public sealed class State
    {
        public string Mode { get; set; } = "normal";
        public bool ProviderRegistered { get; set; }
        public ConcurrentBag<string> ToolUsers { get; } = [];
        public ConcurrentBag<string> ModelInputs { get; } = [];
        public ConcurrentBag<string> Searches { get; } = [];
        public Guid PaymentId { get; } = Guid.NewGuid();
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Model(State state) : IChatClient
    {
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var input = messages.ToArray();
            Assert.DoesNotContain(options!.Tools!, tool => tool.Name is "search" or "fetch");
            if (state.Mode == "custom-record")
                Assert.Equal("Find an authorized payment.", options.Tools!.Single(tool => tool.Name == "payment_lookup").Description);
            state.ModelInputs.Add(System.Text.Json.JsonSerializer.Serialize(input));
            state.Started.TrySetResult();
            if (state.Mode == "error")
                throw new HttpRequestException("provider-secret");
            if (state.Mode == "wait")
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                finally
                {
                    state.Cancelled.TrySetResult();
                }
            }

            var tool = input.LastOrDefault(message => message.Role == ChatRole.Tool);
            var toolName = state.Mode switch
            {
                "report-list" => "ngb_list_reports",
                "report-definition" => "ngb_get_report_definition",
                "report-execute" or "report-denied" => "ngb_execute_report",
                "record-search" => "ngb_search",
                "record-list" or "record-denied" => "ngb_list_records",
                "record-detail" => "ngb_get_record",
                "custom-record" => "payment_lookup",
                _ => "current_user"
            };
            Dictionary<string, object?>? arguments = state.Mode is "report-definition" or "report-execute" or "report-denied"
                ? new() { ["reportCode"] = "pm.aging" }
                : null;
            if (state.Mode is "report-execute" or "report-denied")
                arguments!["request"] = new ReportExecutionRequestDto(Limit: 10);
            if (state.Mode.StartsWith("record-", StringComparison.Ordinal))
            {
                arguments = new() { ["kind"] = "document", ["typeCode"] = "pm.receivable_payment" };
                if (state.Mode == "record-search")
                    arguments["query"] = "RP-2026-000014";
                if (state.Mode == "record-detail")
                    arguments["id"] = state.PaymentId;
            }

            yield return tool is null
                ? new(ChatRole.Assistant, [new FunctionCallContent("call-1", toolName, arguments)]) { FinishReason = ChatFinishReason.ToolCalls }
                : new(ChatRole.Assistant, System.Text.Json.JsonSerializer.Serialize(tool.Contents.OfType<FunctionResultContent>().Single().Result))
                {
                    FinishReason = ChatFinishReason.Stop
                };
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class ObservedModel(IChatClient inner, State state) : DelegatingChatClient(inner)
    {
        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var updates = new List<ChatResponseUpdate>();
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
            {
                updates.Add(update);
                yield return update;
            }

            state.ModelInputs.Add(System.Text.Json.JsonSerializer.Serialize(new
            {
                request = messages.Where(message => message.Role != ChatRole.System),
                response = updates.ToChatResponse()
            }));
        }
    }

    private sealed class SnapshotProvider(IHttpContextAccessor accessor) : IPermissionSnapshotProvider
    {
        public Task<PermissionSnapshot> GetCurrentAsync(CancellationToken ct)
        {
            var user = accessor.HttpContext!.User;
            var subject = user.FindFirstValue("sub");
            return Task.FromResult(new PermissionSnapshot(null, subject, user.Identity?.IsAuthenticated == true,
                subject != "inactive", subject != "restricted", 0, []));
        }
    }

    private sealed class Host(WebApplication app, string origin, State state, X509Certificate2? certificate) : IAsyncDisposable
    {
        private static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes("ai-assistant-test-key-do-not-use-in-production-2026"));
        internal State State => state;

        internal static async Task<Host> StartAsync(
            bool enabled = true,
            bool mcp = true,
            string mode = "normal",
            Dictionary<string, string?>? overrides = null,
            X509Certificate2? certificate = null,
            Action<IServiceCollection>? configure = null)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var origin = $"{(certificate is null ? "http" : "https")}://127.0.0.1:{port}";
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = certificate is null ? Environments.Production : Environments.Development
            });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls(origin);
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureManagement:Mcp"] = mcp.ToString(),
                ["FeatureManagement:AiAssistant"] = enabled.ToString(),
                ["Ngb:Hosting:PublicApiUrl"] = origin,
                ["KeycloakSettings:Issuer"] = origin + "/issuer",
                ["KeycloakSettings:RequireHttpsMetadata"] = "false",
                ["KeycloakSettings:ClientIds:0"] = "ngb-api",
                ["Mcp:AllowLocalhostHttp"] = "true"
            });
            if (overrides is not null)
                builder.Configuration.AddInMemoryCollection(overrides);
            if (certificate is not null)
            {
                builder.Configuration["Ngb:Hosting:PublicApiUrl"] = "https://127.0.0.1:54443";
                builder.Configuration["AiAssistant:McpLoopbackPort"] = port.ToString();
            }
            builder.Services.AddKeycloak(builder.Configuration);
            builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.MapInboundClaims = false;
                options.Configuration = new OpenIdConnectConfiguration { Issuer = origin + "/issuer" };
                options.Configuration.SigningKeys.Add(Key);
                options.ConfigurationManager = null;
                options.TokenValidationParameters.IssuerSigningKey = Key;
                options.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
            });
            builder.Services.AddAuthorization();
            builder.Services.AddCors();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<ICurrentActorContext, HttpCurrentActorContext>();
            builder.Services.AddScoped<IPermissionSnapshotProvider, SnapshotProvider>();
            builder.Services.AddScoped<INgbAccessChecker, NgbAccessChecker>();
            McpTestServices.AddReads(builder.Services);
            if (mode.StartsWith("report-", StringComparison.Ordinal))
            {
                var reports = new Mock<IAuthorizedReportReadService>(MockBehavior.Strict);
                var definition = new ReportDefinitionDto("pm.aging", "Aging");
                reports.Setup(value => value.GetAllDefinitionsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new[] { definition });
                reports.Setup(value => value.GetDefinitionAsync("pm.aging", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(definition);
                var execution = reports.Setup(value => value.ExecuteAsync("pm.aging", It.IsAny<ReportExecutionRequestDto>(), It.IsAny<CancellationToken>()));
                if (mode == "report-denied")
                    execution.ThrowsAsync(new UnauthorizedAccessException());
                else
                    execution.ReturnsAsync(new ReportExecutionResponseDto(new ReportSheetDto([], []), 0, 10, 0, false, null));

                var budget = new Mock<IReportRequestAdmission>();
                budget.SetupGet(value => value.ExecutionTimeout).Returns(TimeSpan.FromSeconds(30));
                budget.Setup(value => value.AcquireAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Mock.Of<IDisposable>());
                builder.Services.AddScoped(_ => reports.Object);
                builder.Services.AddScoped(_ => budget.Object);
            }
            var state = new State { Mode = mode };
            if (mode is "ollama-search" or "record-search")
            {
                var search = new Mock<INgbSearchService>(MockBehavior.Strict);
                search.Setup(value => value.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<NgbSearchKind?>(),
                        It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((string query, int _, NgbSearchKind? kind, string? typeCode, CancellationToken _) =>
                    {
                        state.Searches.Add(query);
                        Assert.Contains("RP-2026-000014", query);
                        Assert.True(kind is null or NgbSearchKind.Document);
                        Assert.True(typeCode is null or "pm.receivable_payment");
                        return new NgbSearchResult([new("document", "pm.receivable_payment", state.PaymentId, PaymentDisplay)], false, []);
                    });
                builder.Services.AddScoped(_ => search.Object);
            }
            if (mode is "record-list" or "record-detail" or "record-denied")
            {
                var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
                var payment = new DocumentDto(state.PaymentId, PaymentDisplay, new RecordPayload(), DocumentStatus.Posted, false);
                documents.Setup(value => value.GetTypeMetadataAsync("pm.receivable_payment", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DocumentTypeMetadataDto("pm.receivable_payment", "Receivable Payment", EntityKind.Document));
                documents.Setup(value => value.GetByIdAsync("pm.receivable_payment", state.PaymentId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(payment);
                var page = documents.Setup(value => value.GetPageAsync("pm.receivable_payment", It.IsAny<PageRequestDto>(), It.IsAny<CancellationToken>()));
                if (mode == "record-denied")
                    page.ThrowsAsync(new UnauthorizedAccessException());
                else
                    page.ReturnsAsync(new PageResponseDto<DocumentDto>([payment], 0, 50, null));

                var scopes = new Mock<IRecordReadScopeFactory>();
                scopes.Setup(value => value.OpenAsync(It.IsAny<BusinessObjectRef>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Mock.Of<IAsyncDisposable>());
                builder.Services.AddScoped(_ => documents.Object);
                builder.Services.AddScoped(_ => scopes.Object);
            }
            builder.Services.AddSingleton(state);
            builder.Services.AddNgbMcpContributor<Contributor>();
            builder.Services.AddScoped<CurrentUserTool>();
            if (mode == "custom-record")
            {
                builder.Services.AddNgbMcpContributor<CustomRecordContributor>();
                builder.Services.AddScoped<CustomRecordTool>();
            }
            builder.Services.AddNgbMcp(builder.Configuration);
            builder.Services.AddNgbAiAssistant(builder.Configuration, services =>
            {
                state.ProviderRegistered = true;
                if (mode.StartsWith("ollama", StringComparison.Ordinal))
                {
                    services.AddNgbOllamaChatClient(_ => { });
                    var registration = services.Single(item => item.ServiceType == typeof(IChatClient));
                    services.Remove(registration);
                    services.AddScoped<IChatClient>(provider => new ObservedModel(
                        (IChatClient)registration.ImplementationFactory!(provider), state));
                }
                else
                    services.AddScoped<IChatClient, Model>();
            });
            configure?.Invoke(builder.Services);
            var app = builder.Build();
            try
            {
                app.UseCors();
                app.UseAuthentication();
                app.UseAuthorization();
                app.MapNgbMcp();
                app.MapNgbAiAssistant();
                await app.StartAsync();
                return new Host(app, origin, state, certificate);
            }
            catch
            {
                await app.DisposeAsync();
                throw;
            }
        }

        internal HttpClient Client(string subject)
        {
            var handler = new HttpClientHandler();
            if (certificate is not null)
            {
                handler.ServerCertificateCustomValidationCallback = (_, remote, _, _) =>
                    remote?.GetCertHashString(HashAlgorithmName.SHA256) == certificate.GetCertHashString(HashAlgorithmName.SHA256);
            }
            var client = new HttpClient(handler) { BaseAddress = new Uri(origin) };
            if (subject != "anonymous")
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(subject));
            return client;
        }

        internal string Token(string subject)
        {
            var claims = new List<Claim> { new("sub", subject), new("typ", "Bearer") };
            if (subject != "service")
                claims.Add(new("auth_time", DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds().ToString()));
            var token = new JwtSecurityToken(origin + "/issuer", "ngb-api", claims,
                DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow.AddMinutes(subject == "expired" ? -1 : 5),
                new SigningCredentials(Key, SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
