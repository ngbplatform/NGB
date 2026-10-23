using System.Net;
using System.Text;
using System.Text.Json;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime.EventStreams;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NGB.AiAssistant.Anthropic;
using NGB.AiAssistant.Bedrock;
using NGB.AiAssistant.Ollama;
using NGB.AiAssistant.OpenAI;
using OllamaSharp.Models.Chat;
using Xunit;
using ChatRole = Microsoft.Extensions.AI.ChatRole;

namespace NGB.AiAssistant.Providers.Tests;

public sealed class ProviderContractTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OllamaCanDisableReasoningForABoundedAnswerRecovery(bool streaming)
    {
        var handler = new RecordingHandler([
            """
            {"model":"contract-model","message":{"role":"assistant","content":"Done."},"done":true,"done_reason":"stop"}

            """]);
        await using var services = Register("Ollama", handler).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IChatClient>();
        var options = new ChatOptions { Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None } };
        ChatMessage[] messages = [new(ChatRole.User, "Answer from the retained evidence.")];

        var response = streaming
            ? await client.GetStreamingResponseAsync(messages, options).ToChatResponseAsync()
            : await client.GetResponseAsync(messages, options);

        Assert.Equal("Done.", response.Text);
        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.False(body.RootElement.GetProperty("think").GetBoolean());
        Assert.Equal(ReasoningEffort.None, options.Reasoning.Effort);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OllamaPreservesReasoningAndMatchesEachToolResultToItsName(bool streaming)
    {
        var handler = new RecordingHandler([
            """
            {"model":"contract-model","message":{"role":"assistant","content":"Done."},"done":true,"done_reason":"stop"}

            """]);
        await using var services = Register("Ollama", handler).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IChatClient>();
        var options = new ChatOptions
        {
            Instructions = "Keep the requested scope.",
            Temperature = 0.25f,
            RawRepresentationFactory = _ => new ChatRequest { Think = false, KeepAlive = "5m" }
        };
        ChatMessage[] messages =
        [
            new(ChatRole.User, "Read these records."),
            new(ChatRole.Assistant,
            [
                new TextReasoningContent("Use the two selected records."),
                new FunctionCallContent("first", "read_record", new Dictionary<string, object?> { ["id"] = "one" }),
                new FunctionCallContent("second", "read_notes", new Dictionary<string, object?> { ["id"] = "two" })
            ]),
            new(ChatRole.Tool,
            [
                new FunctionResultContent("second", new { count = 3 }),
                new FunctionResultContent("first", new { count = 1 })
            ])
        ];

        var response = streaming
            ? await client.GetStreamingResponseAsync(messages, options).ToChatResponseAsync()
            : await client.GetResponseAsync(messages, options);

        Assert.Equal("Done.", response.Text);
        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        var root = body.RootElement;
        Assert.False(root.GetProperty("think").GetBoolean());
        Assert.Equal("5m", root.GetProperty("keep_alive").GetString());
        Assert.Equal(0.25f, root.GetProperty("options").GetProperty("temperature").GetSingle());
        var sent = root.GetProperty("messages");
        Assert.Equal(5, sent.GetArrayLength());
        Assert.Equal("system", sent[0].GetProperty("role").GetString());
        Assert.Equal(options.Instructions, sent[0].GetProperty("content").GetString());
        Assert.Equal("Use the two selected records.", sent[2].GetProperty("thinking").GetString());
        Assert.Equal(2, sent[2].GetProperty("tool_calls").GetArrayLength());
        Assert.Equal("read_notes", sent[3].GetProperty("tool_name").GetString());
        Assert.Equal("read_record", sent[4].GetProperty("tool_name").GetString());
        using var result = JsonDocument.Parse(sent[3].GetProperty("content").GetString()!);
        Assert.Equal(3, result.RootElement.GetProperty("count").GetInt32());
        Assert.Equal("Keep the requested scope.", options.Instructions);
    }

    [Theory]
    [InlineData("Ollama")]
    [InlineData("OpenAI")]
    [InlineData("Anthropic")]
    public async Task ProviderPreservesNestedDictionaryToolSchemas(string provider)
    {
        var schema = JsonSerializer.Deserialize<JsonElement>("""
            {
              "type": "object",
              "properties": {
                "request": {
                  "type": "object",
                  "properties": {
                    "filters": {
                      "type": ["object", "null"],
                      "additionalProperties": {
                        "type": "object",
                        "properties": { "value": true },
                        "required": ["value"],
                        "additionalProperties": false
                      }
                    }
                  }
                }
              },
              "required": ["request"]
            }
            """);
        var handler = new RecordingHandler(Responses(provider));
        await using var services = Register(provider, handler).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IChatClient>();

        await client.GetStreamingResponseAsync([new(ChatRole.User, "Run the report")],
            new ChatOptions { Tools = [new SchemaTool(schema)] }).ToChatResponseAsync();

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        var tool = body.RootElement.GetProperty("tools")[0];
        var actual = provider == "Anthropic"
            ? tool.GetProperty("input_schema")
            : tool.GetProperty("function").GetProperty("parameters");
        var filters = actual.GetProperty("properties").GetProperty("request")
            .GetProperty("properties").GetProperty("filters");
        Assert.Equal(["object", "null"], filters.GetProperty("type").EnumerateArray().Select(value => value.GetString()));
        var entry = filters.GetProperty("additionalProperties");
        Assert.Equal("object", entry.GetProperty("type").GetString());
        Assert.Equal(["value"], entry.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
        Assert.False(entry.GetProperty("additionalProperties").GetBoolean());
        var valueSchema = entry.GetProperty("properties").GetProperty("value");
        // Both forms permit any JSON value; SDKs may normalize the boolean schema.
        Assert.True(valueSchema.ValueKind == JsonValueKind.True
            || valueSchema.ValueKind == JsonValueKind.Object && !valueSchema.EnumerateObject().Any());
    }

    [Theory]
    [InlineData("Ollama")]
    [InlineData("OpenAI")]
    [InlineData("Anthropic")]
    public async Task NativeAdapterStreamsToolsAndReturnsTheirResults(string provider)
    {
        var handler = new RecordingHandler(Responses(provider));
        await using var services = Register(provider, handler).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IChatClient>();
        var tool = AIFunctionFactory.Create((string typeCode) => typeCode, "read_records");
        var options = new ChatOptions { Tools = [tool], MaxOutputTokens = 128, AllowMultipleToolCalls = false };
        var messages = new List<ChatMessage> { new(ChatRole.System, "Read business records."), new(ChatRole.User, "Show records") };

        var first = await client.GetStreamingResponseAsync(messages, options).ToChatResponseAsync();
        var call = Assert.Single(first.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>());
        Assert.Equal("read_records", call.Name);
        Assert.Equal("pm.property", call.Arguments!["typeCode"]!.ToString());
        messages.AddRange(first.Messages);
        messages.Add(new(ChatRole.Tool, [new FunctionResultContent(call.CallId, new { count = 7 })]));

        var second = await client.GetStreamingResponseAsync(messages, options).ToChatResponseAsync();

        Assert.Equal("Seven records.", second.Text);
        Assert.Equal(2, handler.Requests.Count);
        using var firstBody = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.Equal("contract-model", firstBody.RootElement.GetProperty("model").GetString());
        Assert.True(firstBody.RootElement.GetProperty("stream").GetBoolean());
        Assert.Contains("read_records", handler.Requests[0].Body);
        Assert.Contains("pm.property", handler.Requests[1].Body);
        Assert.Contains("7", handler.Requests[1].Body);
        Assert.DoesNotContain("Bearer user-token", string.Join(' ', handler.Requests.Select(request => request.Authorization)));
        if (provider == "Ollama")
        {
            Assert.Null(handler.Requests[0].Authorization);
            Assert.False(firstBody.RootElement.TryGetProperty("think", out _));
            Assert.Equal(0, firstBody.RootElement.GetProperty("options").GetProperty("temperature").GetSingle());
            Assert.Equal(32768, firstBody.RootElement.GetProperty("options").GetProperty("num_ctx").GetInt32());
        }
        else if (provider == "OpenAI")
        {
            Assert.Equal("Bearer provider-key", handler.Requests[0].Authorization);
            Assert.Equal("/v1/chat/completions", handler.Requests[0].Uri.AbsolutePath);
        }
        else
        {
            Assert.Equal("provider-key", handler.Requests[0].ApiKey);
            Assert.Equal("/v1/messages", handler.Requests[0].Uri.AbsolutePath);
        }
    }

    [Fact]
    public async Task BedrockStreamsNativeConverseEventsAndTransmitsToolResults()
    {
        var runtime = new Mock<IAmazonBedrockRuntime>();
        var requests = new List<ConverseStreamRequest>();
        runtime.Setup(client => client.ConverseStreamAsync(It.IsAny<ConverseStreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns((ConverseStreamRequest request, CancellationToken _) =>
            {
                requests.Add(request);
                var events = requests.Count == 1
                    ? new (string, string)[]
                    {
                        ("messageStart", "{\"role\":\"assistant\"}"),
                        ("contentBlockStart", "{\"contentBlockIndex\":0,\"start\":{\"toolUse\":{\"toolUseId\":\"call-1\",\"name\":\"read_records\"}}}"),
                        ("contentBlockDelta", "{\"contentBlockIndex\":0,\"delta\":{\"toolUse\":{\"input\":\"{\\\"typeCode\\\":\\\"pm.property\\\"}\"}}}"),
                        ("contentBlockStop", "{\"contentBlockIndex\":0}"),
                        ("messageStop", "{\"stopReason\":\"tool_use\"}")
                    }
                    : [
                        ("messageStart", "{\"role\":\"assistant\"}"),
                        ("contentBlockDelta", "{\"contentBlockIndex\":0,\"delta\":{\"text\":\"Seven records.\"}}"),
                        ("contentBlockStop", "{\"contentBlockIndex\":0}"),
                        ("messageStop", "{\"stopReason\":\"end_turn\"}")
                    ];
                var bytes = events.SelectMany(item => BedrockEvent(item.Item1, item.Item2)).ToArray();
                return Task.FromResult(new ConverseStreamResponse { Stream = new ConverseStreamOutput(new MemoryStream(bytes)) });
            });
        var services = new ServiceCollection();
        services.AddSingleton(runtime.Object);
        services.AddNgbBedrockChatClient(options =>
        {
            options.Region = "us-east-1";
            options.Model = "contract-model";
        });
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IChatClient>();
        var options = new ChatOptions { Tools = [AIFunctionFactory.Create((string typeCode) => typeCode, "read_records")], MaxOutputTokens = 128 };
        var messages = new List<ChatMessage> { new(ChatRole.User, "Show records") };

        var first = await client.GetStreamingResponseAsync(messages, options).ToChatResponseAsync();
        var call = Assert.Single(first.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>());
        Assert.Equal("read_records", call.Name);
        messages.AddRange(first.Messages);
        messages.Add(new(ChatRole.Tool, [new FunctionResultContent(call.CallId, new { count = 7 })]));
        var second = await client.GetStreamingResponseAsync(messages, options).ToChatResponseAsync();

        Assert.Equal("Seven records.", second.Text);
        Assert.Equal("contract-model", requests[0].ModelId);
        Assert.Equal(128, requests[0].InferenceConfig.MaxTokens);
        Assert.Equal("read_records", Assert.Single(requests[0].ToolConfig.Tools).ToolSpec.Name);
        Assert.Contains(requests[1].Messages.SelectMany(message => message.Content), content => content.ToolResult?.ToolUseId == call.CallId);
    }

    [Fact]
    public async Task BedrockTransmitsBothOutputBudgetsAndMapsTruncationSeparatelyFromCompletion()
    {
        var runtime = new Mock<IAmazonBedrockRuntime>();
        var budgets = new List<int?>();
        runtime.Setup(client => client.ConverseStreamAsync(It.IsAny<ConverseStreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns((ConverseStreamRequest request, CancellationToken _) =>
            {
                budgets.Add(request.InferenceConfig.MaxTokens);
                var reason = budgets.Count == 1 ? "max_tokens" : "end_turn";
                var events = new (string, string)[]
                {
                    ("messageStart", "{\"role\":\"assistant\"}"),
                    ("contentBlockDelta", "{\"contentBlockIndex\":0,\"delta\":{\"text\":\"Answer\"}}"),
                    ("contentBlockStop", "{\"contentBlockIndex\":0}"),
                    ("messageStop", JsonSerializer.Serialize(new { stopReason = reason }))
                };
                var bytes = events.SelectMany(item => BedrockEvent(item.Item1, item.Item2)).ToArray();
                return Task.FromResult(new ConverseStreamResponse { Stream = new ConverseStreamOutput(new MemoryStream(bytes)) });
            });
        var services = new ServiceCollection();
        services.AddSingleton(runtime.Object);
        services.AddNgbBedrockChatClient(options =>
        {
            options.Region = "us-east-1";
            options.Model = "contract-model";
        });
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IChatClient>();
        ChatMessage[] messages = [new(ChatRole.User, "Show the complete table.")];

        var truncated = await client.GetStreamingResponseAsync(messages, new() { MaxOutputTokens = 2048 }).ToChatResponseAsync();
        var complete = await client.GetStreamingResponseAsync(messages, new() { MaxOutputTokens = 8192 }).ToChatResponseAsync();

        Assert.Equal(new int?[] { 2048, 8192 }, budgets);
        Assert.Equal(ChatFinishReason.Length, truncated.FinishReason);
        Assert.Equal(ChatFinishReason.Stop, complete.FinishReason);
    }

    [Theory]
    [InlineData("Ollama")]
    [InlineData("OpenAI")]
    [InlineData("Anthropic")]
    public async Task CancellationReachesTheProviderTransport(string provider)
    {
        var handler = new RecordingHandler([]) { Wait = true };
        await using var services = Register(provider, handler).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        using var cancellation = new CancellationTokenSource();
        var client = scope.ServiceProvider.GetRequiredService<IChatClient>();
        var response = client.GetStreamingResponseAsync([new(ChatRole.User, "Hello")], new() { MaxOutputTokens = 128 }, cancellation.Token)
            .ToChatResponseAsync(cancellationToken: cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await response);
        Assert.True(handler.Cancelled);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("http://provider.example/v1")]
    [InlineData("https://user:secret@provider.example/v1")]
    [InlineData("https://provider.example/v1?key=secret")]
    [InlineData("https://provider.example/v1#fragment")]
    [InlineData("http://localhost:11434/v1")]
    [InlineData("http://host.docker.internal:11434/v1")]
    [InlineData("ftp://provider.example/v1")]
    [InlineData("relative/path")]
    public void RejectsUnsafeCloudEndpoints(string endpoint)
    {
        var services = new ServiceCollection().AddNgbOpenAiChatClient(options =>
        {
            options.Endpoint = endpoint;
            options.Model = "model";
            options.ApiKey = "key";
        });
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<OpenAiAssistantOptions>>().Value);
    }

    [Theory]
    [InlineData("http://localhost:11434", true)]
    [InlineData("http://127.0.0.1:11434", true)]
    [InlineData("http://[::1]:11434", true)]
    [InlineData("http://host.docker.internal:11434", false)]
    [InlineData("http://HOST.DOCKER.INTERNAL:11434", false)]
    [InlineData("https://ollama.example/api", true)]
    [InlineData("http://ollama.example:11434", false)]
    [InlineData("http://192.168.1.10:11434", false)]
    [InlineData("http://host.docker.internal.evil.example:11434", false)]
    [InlineData("http://localhost.evil.example:11434", false)]
    [InlineData("http://user:secret@localhost:11434", false)]
    [InlineData("http://host.docker.internal:11434?key=secret", false)]
    [InlineData("http://localhost:11434#fragment", false)]
    [InlineData("ftp://localhost:11434", false)]
    [InlineData("relative/path", false)]
    public void OllamaEndpointPolicyDefaultsToHttpsOrLoopback(string endpoint, bool valid)
    {
        var services = new ServiceCollection().AddNgbOllamaChatClient(options => options.Endpoint = endpoint);
        using var provider = services.BuildServiceProvider();

        if (valid)
            Assert.Equal(endpoint, provider.GetRequiredService<IOptions<OllamaAssistantOptions>>().Value.Endpoint);
        else
            Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<OllamaAssistantOptions>>().Value);
    }

    [Theory]
    [InlineData("http://host.docker.internal:11434", true)]
    [InlineData("http://ollama.internal:11434", true)]
    [InlineData("http://192.168.1.10:11434", true)]
    [InlineData("http://localhost:11434", true)]
    [InlineData("https://ollama.example/api", true)]
    [InlineData("http://user:secret@ollama.internal:11434", false)]
    [InlineData("http://ollama.internal:11434?key=secret", false)]
    [InlineData("http://ollama.internal:11434#fragment", false)]
    [InlineData("ftp://ollama.internal:11434", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("relative/path", false)]
    [InlineData("", false)]
    public void OllamaHttpOptInPreservesEndpointValidation(string endpoint, bool valid)
    {
        var services = new ServiceCollection().AddNgbOllamaChatClient(options =>
        {
            options.Endpoint = endpoint;
            options.AllowInsecureHttp = true;
        });
        using var provider = services.BuildServiceProvider();

        if (valid)
            Assert.Equal(endpoint, provider.GetRequiredService<IOptions<OllamaAssistantOptions>>().Value.Endpoint);
        else
            Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<OllamaAssistantOptions>>().Value);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public void OllamaBindsHttpOptInAndValidatesItAtStartup(string? configuredValue, bool allowed)
    {
        const string endpoint = "http://ollama.internal:11434";
        var values = new Dictionary<string, string?>
        {
            [OllamaAssistantOptions.SectionName + ":Endpoint"] = endpoint
        };
        if (configuredValue is not null)
            values[OllamaAssistantOptions.SectionName + ":AllowInsecureHttp"] = configuredValue;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection().AddNgbOllamaChatClient(
            configuration.GetSection(OllamaAssistantOptions.SectionName).Bind);
        using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<IStartupValidator>();

        if (!allowed)
        {
            Assert.Throws<OptionsValidationException>(validator.Validate);
            return;
        }

        validator.Validate();
        using var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(Ollama.DependencyInjection.HttpClientName);
        Assert.Equal(new Uri(endpoint), http.BaseAddress);
    }

    private static ServiceCollection Register(string provider, RecordingHandler handler)
    {
        var services = new ServiceCollection();
        string name;
        switch (provider)
        {
            case "Ollama":
                services.AddNgbOllamaChatClient(options => options.Model = "contract-model");
                name = Ollama.DependencyInjection.HttpClientName;
                break;
            case "OpenAI":
                services.AddNgbOpenAiChatClient(options =>
                {
                    options.Model = "contract-model";
                    options.ApiKey = "provider-key";
                    options.Endpoint = "https://compatible.example/v1";
                });
                name = OpenAI.DependencyInjection.HttpClientName;
                break;
            default:
                services.AddNgbAnthropicChatClient(options =>
                {
                    options.Model = "contract-model";
                    options.ApiKey = "provider-key";
                });
                name = Anthropic.DependencyInjection.HttpClientName;
                break;
        }

        services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services;
    }

    private static string[] Responses(string provider)
        => provider switch
        {
            "Ollama" =>
            [
                """
                {"model":"contract-model","message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"read_records","arguments":{"typeCode":"pm.property"}}}]},"done":false}
                {"model":"contract-model","message":{"role":"assistant","content":""},"done":true,"done_reason":"stop"}

                """,
                """
                {"model":"contract-model","message":{"role":"assistant","content":"Seven records."},"done":false}
                {"model":"contract-model","message":{"role":"assistant","content":""},"done":true,"done_reason":"stop"}

                """
            ],
            "OpenAI" =>
            [
                """
                data: {"id":"chat-1","object":"chat.completion.chunk","created":1,"model":"contract-model","choices":[{"index":0,"delta":{"role":"assistant","tool_calls":[{"index":0,"id":"call-1","type":"function","function":{"name":"read_records","arguments":"{\"typeCode\":\"pm.property\"}"}}]},"finish_reason":null}]}

                data: {"id":"chat-1","object":"chat.completion.chunk","created":1,"model":"contract-model","choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}]}

                data: [DONE]


                """,
                """
                data: {"id":"chat-2","object":"chat.completion.chunk","created":1,"model":"contract-model","choices":[{"index":0,"delta":{"role":"assistant","content":"Seven records."},"finish_reason":null}]}

                data: {"id":"chat-2","object":"chat.completion.chunk","created":1,"model":"contract-model","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}

                data: [DONE]


                """
            ],
            _ =>
            [
                """
                event: message_start
                data: {"type":"message_start","message":{"id":"msg-1","type":"message","role":"assistant","content":[],"model":"contract-model","stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":10,"output_tokens":0}}}

                event: content_block_start
                data: {"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"call-1","name":"read_records","input":{}}}

                event: content_block_delta
                data: {"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{\"typeCode\":\"pm.property\"}"}}

                event: content_block_stop
                data: {"type":"content_block_stop","index":0}

                event: message_delta
                data: {"type":"message_delta","delta":{"stop_reason":"tool_use","stop_sequence":null},"usage":{"output_tokens":10}}

                event: message_stop
                data: {"type":"message_stop"}


                """,
                """
                event: message_start
                data: {"type":"message_start","message":{"id":"msg-2","type":"message","role":"assistant","content":[],"model":"contract-model","stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":10,"output_tokens":0}}}

                event: content_block_start
                data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

                event: content_block_delta
                data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Seven records."}}

                event: content_block_stop
                data: {"type":"content_block_stop","index":0}

                event: message_delta
                data: {"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":10}}

                event: message_stop
                data: {"type":"message_stop"}


                """
            ]
        };

    private static byte[] BedrockEvent(string type, string json)
    {
        var headers = new List<IEventStreamHeader>();
        foreach (var (name, value) in new[] { (":message-type", "event"), (":event-type", type), (":content-type", "application/json") })
        {
            var header = new EventStreamHeader(name);
            header.SetString(value);
            headers.Add(header);
        }

        return new EventStreamMessage(headers, Encoding.UTF8.GetBytes(json)).ToByteArray();
    }

    private sealed record RecordedRequest(Uri Uri, string Body, string? Authorization, string? ApiKey);

    private sealed class SchemaTool(JsonElement schema) : AIFunction
    {
        public override string Name => "read_records";
        public override JsonElement JsonSchema => schema;

        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class RecordingHandler(string[] responses) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Wait { get; init; }
        public bool Cancelled { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new(request.RequestUri!, await request.Content!.ReadAsStringAsync(cancellationToken),
                request.Headers.Authorization?.ToString(), request.Headers.TryGetValues("x-api-key", out var keys) ? keys.Single() : null));
            Started.TrySetResult();
            if (Wait)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Cancelled = true;
                    throw;
                }
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responses[Requests.Count - 1], Encoding.UTF8,
                    request.RequestUri!.AbsolutePath == "/api/chat" ? "application/x-ndjson" : "text/event-stream")
            };
        }
    }
}
