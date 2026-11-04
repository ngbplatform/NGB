using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using Amazon.Runtime.EventStreams;
using EquipmentInspection.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentInspection.IntegrationTests;

// Only the vendor transport is replaced. NGB's published adapters, runner and MCP remain real.
internal sealed class AssistantProviderFixture(string provider)
{
    public const string Model = "example-contract-model";
    public const string ApiKey = "example-fixture-key";
    public const string Answer = "The equipment record is available.";
    public const string FailureDetail = "private-provider-diagnostic-example-fixture-key";

    public ConcurrentQueue<ProviderRequest> Requests { get; } = new();
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Guid EquipmentId { get; set; }
    public bool Wait { get; set; }
    public bool Fail { get; set; }

    private string Provider => provider;

    public static Dictionary<string, string> Configuration(string provider) => new()
    {
        ["FeatureManagement:Mcp"] = "true",
        ["FeatureManagement:AiAssistant"] = "true",
        ["AiAssistant:Provider"] = provider,
        ["AiAssistant:InitialOutputTokens"] = "128",
        ["AiAssistant:MaxOutputTokens"] = "256",
        ["AiAssistant:MaxMessages"] = "4",
        ["AiAssistant:MaxMessageCharacters"] = "1024",
        ["AiAssistant:MaxInputBytes"] = "4096",
        ["AiAssistant:OpenAI:Model"] = Model,
        ["AiAssistant:OpenAI:ApiKey"] = ApiKey,
        ["AiAssistant:OpenAI:Endpoint"] = "https://provider.example.test/v1",
        ["AiAssistant:Anthropic:Model"] = Model,
        ["AiAssistant:Anthropic:ApiKey"] = ApiKey,
        ["AiAssistant:Bedrock:Model"] = Model,
        ["AiAssistant:Bedrock:Region"] = "us-east-1",
        ["AiAssistant:Ollama:Model"] = Model,
        ["AiAssistant:Ollama:Endpoint"] = "http://localhost:11434",
        ["AiAssistant:Ollama:ContextLength"] = "16384"
    };

    public static IConfiguration Bind(IReadOnlyDictionary<string, string> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values.Select(item =>
            new KeyValuePair<string, string?>(item.Key, item.Value))).Build();

    public void Register(IServiceCollection services)
    {
        if (provider == "Bedrock")
        {
            services.AddScoped<IAmazonBedrockRuntime>(_ => new BedrockTransport(this));
            return;
        }

        var name = provider switch
        {
            "OpenAI" => NGB.AiAssistant.OpenAI.DependencyInjection.HttpClientName,
            "Anthropic" => NGB.AiAssistant.Anthropic.DependencyInjection.HttpClientName,
            "Ollama" => NGB.AiAssistant.Ollama.DependencyInjection.HttpClientName,
            _ => throw new InvalidOperationException("Unknown test provider.")
        };
        services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => new HttpTransport(this));
    }

    private async Task RecordAsync(ProviderRequest request, CancellationToken ct)
    {
        Requests.Enqueue(request);
        Started.TrySetResult();
        if (Fail)
            throw new HttpRequestException(FailureDetail);

        if (Wait)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                throw;
            }
        }
    }

    private string HttpResponse(bool toolResult)
    {
        var arguments = new { id = EquipmentId };
        var function = new { name = EquipmentReadToolContributor.ToolName, arguments };
        if (provider == "Ollama")
        {
            object message = toolResult
                ? new { role = "assistant", content = Answer }
                : new { role = "assistant", content = "", tool_calls = new[] { new { function } } };
            return JsonSerializer.Serialize(new { model = Model, message, done = true, done_reason = "stop" }) + "\n";
        }

        if (provider == "OpenAI")
        {
            object delta = toolResult
                ? new { role = "assistant", content = Answer }
                : new
                {
                    role = "assistant",
                    tool_calls = new[]
                    {
                        new
                        {
                            index = 0,
                            id = "example-call",
                            type = "function",
                            function = new { function.name, arguments = JsonSerializer.Serialize(arguments) }
                        }
                    }
                };
            var chunk = JsonSerializer.Serialize(new
            {
                id = "example-chat",
                @object = "chat.completion.chunk",
                created = 1,
                model = Model,
                choices = new[] { new { index = 0, delta, finish_reason = toolResult ? "stop" : "tool_calls" } }
            });
            return $"data: {chunk}\n\ndata: [DONE]\n\n";
        }

        var result = new StringBuilder();
        void Event(string type, object value)
        {
            result.Append("event: ").Append(type).Append('\n');
            result.Append("data: ").Append(JsonSerializer.Serialize(value)).Append("\n\n");
        }

        Event("message_start", new
        {
            type = "message_start",
            message = new
            {
                id = "example-message",
                type = "message",
                role = "assistant",
                content = Array.Empty<object>(),
                model = Model,
                usage = new { input_tokens = 10, output_tokens = 0 }
            }
        });
        object block = toolResult
            ? new { type = "text", text = "" }
            : new { type = "tool_use", id = "example-call", function.name, input = new { } };
        Event("content_block_start", new { type = "content_block_start", index = 0, content_block = block });
        object content = toolResult
            ? new { type = "text_delta", text = Answer }
            : new { type = "input_json_delta", partial_json = JsonSerializer.Serialize(arguments) };
        Event("content_block_delta", new { type = "content_block_delta", index = 0, delta = content });
        Event("content_block_stop", new { type = "content_block_stop", index = 0 });
        Event("message_delta", new
        {
            type = "message_delta",
            delta = new { stop_reason = toolResult ? "end_turn" : "tool_use" },
            usage = new { output_tokens = 10 }
        });
        Event("message_stop", new { type = "message_stop" });
        return result.ToString();
    }

    private sealed class HttpTransport(AssistantProviderFixture fixture) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            await fixture.RecordAsync(new(request.RequestUri!, body,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("x-api-key", out var keys) ? keys.Single() : null), ct);
            using var json = JsonDocument.Parse(body);
            var messages = json.RootElement.GetProperty("messages");
            var toolResult = messages.EnumerateArray().Any(message =>
                message.GetProperty("role").GetString() == "tool"
                || message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
                && content.EnumerateArray().Any(item => item.GetProperty("type").GetString() == "tool_result"));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(fixture.HttpResponse(toolResult), Encoding.UTF8,
                    fixture.Provider == "Ollama" ? "application/x-ndjson" : "text/event-stream")
            };
        }
    }

    private sealed class BedrockTransport(AssistantProviderFixture fixture)
        : AmazonBedrockRuntimeClient(new AnonymousAWSCredentials(), RegionEndpoint.USEast1)
    {
        public override async Task<ConverseStreamResponse> ConverseStreamAsync(ConverseStreamRequest request,
            CancellationToken cancellationToken = default)
        {
            await fixture.RecordAsync(new(new Uri("https://bedrock-runtime.us-east-1.amazonaws.com"),
                JsonSerializer.Serialize(request), null, null), cancellationToken);
            var toolResult = request.Messages.SelectMany(message => message.Content).Any(content => content.ToolResult is not null);
            var events = new List<(string Type, object Value)> { ("messageStart", new { role = "assistant" }) };
            if (toolResult)
            {
                events.Add(("contentBlockDelta", new { contentBlockIndex = 0, delta = new { text = Answer } }));
            }
            else
            {
                events.Add(("contentBlockStart", new
                {
                    contentBlockIndex = 0,
                    start = new { toolUse = new { toolUseId = "example-call", name = EquipmentReadToolContributor.ToolName } }
                }));
                events.Add(("contentBlockDelta", new
                {
                    contentBlockIndex = 0,
                    delta = new { toolUse = new { input = JsonSerializer.Serialize(new { id = fixture.EquipmentId }) } }
                }));
            }
            events.Add(("contentBlockStop", new { contentBlockIndex = 0 }));
            events.Add(("messageStop", new { stopReason = toolResult ? "end_turn" : "tool_use" }));
            var bytes = events.SelectMany(item => EncodeEvent(item.Type, item.Value)).ToArray();
            return new ConverseStreamResponse { Stream = new ConverseStreamOutput(new MemoryStream(bytes)) };
        }

        private static byte[] EncodeEvent(string type, object value)
        {
            var headers = new List<IEventStreamHeader>();
            foreach (var (name, text) in new[] { (":message-type", "event"), (":event-type", type), (":content-type", "application/json") })
            {
                var header = new EventStreamHeader(name);
                header.SetString(text);
                headers.Add(header);
            }
            return new EventStreamMessage(headers, JsonSerializer.SerializeToUtf8Bytes(value)).ToByteArray();
        }
    }

    internal sealed record ProviderRequest(Uri Uri, string Body, string? Authorization, string? ApiKey);
}
