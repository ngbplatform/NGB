using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace NGB.AiAssistant.AspNetCore.IntegrationTests;

public sealed partial class AssistantHttpTests
{
    [Fact]
    public async Task RateLimitStillAppliesAfterThePreviousRequestHasCompleted()
    {
        await using var host = await Host.StartAsync(overrides: new() { ["AiAssistant:RequestsPerMinute"] = "1" });
        using var client = host.Client("alex");
        using var first = await client.PostAsJsonAsync("/api/ai-assistant/chat", Question);
        Assert.Contains("\"type\":\"done\"", await first.Content.ReadAsStringAsync());

        using var second = await client.PostAsJsonAsync("/api/ai-assistant/chat", Question);

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(5), second.Headers.RetryAfter!.Delta);
        Assert.Single(host.State.ToolUsers);
    }

    [Fact]
    public async Task UnsupportedContentTypeReleasesAdmissionWithoutInvokingTheModel()
    {
        await using var host = await Host.StartAsync();
        using var client = host.Client("alex");
        using var invalid = await client.PostAsync("/api/ai-assistant/chat", new StringContent("hello"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, invalid.StatusCode);
        Assert.Empty(host.State.ModelInputs);

        using var valid = await client.PostAsJsonAsync("/api/ai-assistant/chat", Question);
        Assert.Contains("\"type\":\"done\"", await valid.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ChunkedBodyCannotBypassTheInputByteLimit()
    {
        await using var host = await Host.StartAsync();
        using var client = host.Client("alex");
        using var content = new UnknownLengthContent(new string('x', 40000));
        content.Headers.ContentType = new("application/json");

        using var response = await client.PostAsync("/api/ai-assistant/chat", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("input_limit", (await response.Content.ReadFromJsonAsync<AiAssistantEvent>())!.Code);
        Assert.Empty(host.State.ModelInputs);
    }

    [Fact]
    public async Task NullJsonBodyIsRejectedBeforeOpeningMcp()
    {
        await using var host = await Host.StartAsync();
        using var client = host.Client("alex");

        using var response = await client.PostAsync("/api/ai-assistant/chat", new StringContent("null", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.State.ToolUsers);
        Assert.Empty(host.State.ModelInputs);
    }

    [Fact]
    public async Task ProviderTimeoutProducesATerminalErrorAndReleasesAdmission()
    {
        await using var host = await Host.StartAsync(mode: "wait", overrides: new() { ["AiAssistant:RequestTimeoutSeconds"] = "1" });
        using var client = host.Client("alex");

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", Question);
        var events = ReadEvents(await response.Content.ReadAsStringAsync());

        Assert.Equal("timeout", Assert.Single(events, item => item.Type == "error").Code);
        Assert.DoesNotContain(events, item => item.Type == "done");
        host.State.Mode = "normal";
        using var retry = await client.PostAsJsonAsync("/api/ai-assistant/chat", Question);
        Assert.Contains("\"type\":\"done\"", await retry.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SettingsExposeConfiguredProviderIdentityWithoutOpeningAConversation()
    {
        await using var host = await Host.StartAsync(configure: services =>
            services.AddSingleton(new AiAssistantModelInfo("Bedrock", "test-model")));
        using var client = host.Client("alex");

        var settings = await client.GetFromJsonAsync<JsonElement>("/api/ai-assistant");

        Assert.Equal("Bedrock", settings.GetProperty("provider").GetString());
        Assert.Equal("test-model", settings.GetProperty("model").GetString());
        Assert.Empty(host.State.ModelInputs);
        Assert.Empty(host.State.ToolUsers);
    }

    [Fact]
    public void EnabledAssistantRequiresARegisteredProvider()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FeatureManagement:AiAssistant"] = "true",
            ["FeatureManagement:Mcp"] = "true"
        }).Build();
        var services = new ServiceCollection();

        var error = Assert.Throws<InvalidOperationException>(() => services.AddNgbAiAssistant(configuration, _ => { }));

        Assert.Contains(nameof(IChatClient), error.Message);
    }

    private sealed class UnknownLengthContent(string value) : HttpContent
    {
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(value));
        }
    }
}
