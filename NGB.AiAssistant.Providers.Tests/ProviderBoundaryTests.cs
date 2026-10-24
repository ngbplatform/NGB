using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NGB.AiAssistant.Anthropic;
using NGB.AiAssistant.Bedrock;
using NGB.AiAssistant.Ollama;
using OllamaSharp.Models.Chat;
using Xunit;
using ChatRole = Microsoft.Extensions.AI.ChatRole;

namespace NGB.AiAssistant.Providers.Tests;

public sealed partial class ProviderContractTests
{
    [Theory]
    [InlineData("", "key")]
    [InlineData("model", "")]
    public void AnthropicRejectsMissingModelOrCredentials(string model, string key)
    {
        using var provider = new ServiceCollection().AddNgbAnthropicChatClient(options =>
        {
            options.Model = model;
            options.ApiKey = key;
        }).BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
    }

    [Theory]
    [InlineData("", "us-east-1")]
    [InlineData("model", "not-an-aws-region")]
    public void BedrockRejectsMissingModelOrUnknownRegion(string model, string region)
    {
        using var provider = new ServiceCollection().AddNgbBedrockChatClient(options =>
        {
            options.Model = model;
            options.Region = region;
        }).BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
    }

    [Theory]
    [InlineData("", 8192)]
    [InlineData("model-cloud", 8192)]
    [InlineData("local-model", 0)]
    [InlineData("local-model", OllamaAssistantOptions.MaximumContextLength + 1)]
    public void OllamaRejectsInvalidModelAndContextLimits(string model, int contextLength)
    {
        using var provider = new ServiceCollection().AddNgbOllamaChatClient(options =>
        {
            options.Model = model;
            options.ContextLength = contextLength;
        }).BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OllamaSupportsDefaultOptionsAndImageOnlyMessages(bool streaming)
    {
        var handler = new RecordingHandler([
            """
            {"model":"contract-model","message":{"role":"assistant","content":"Done."},"done":true,"done_reason":"stop"}

            """]);
        await using var provider = Register("Ollama", handler).BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IChatClient>();
        byte[] image = [1, 2, 3];
        ChatMessage[] messages =
        [
            new(ChatRole.User, [new DataContent(image, "image/png")]),
            new(ChatRole.User, [new DataContent(new byte[] { 4, 5 }, "application/octet-stream")])
        ];

        var response = streaming
            ? await client.GetStreamingResponseAsync(messages).ToChatResponseAsync()
            : await client.GetResponseAsync(messages);

        Assert.Equal("Done.", response.Text);
        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        var sent = body.RootElement.GetProperty("messages");
        Assert.Equal(1, sent.GetArrayLength());
        Assert.Equal(Convert.ToBase64String(image), sent[0].GetProperty("images")[0].GetString());
        Assert.Equal(0, body.RootElement.GetProperty("options").GetProperty("temperature").GetDouble());
    }

    [Fact]
    public async Task OllamaKeepsMessagesFromTheNativeRequestFactory()
    {
        var handler = new RecordingHandler([
            """
            {"model":"contract-model","message":{"role":"assistant","content":"Done."},"done":true,"done_reason":"stop"}

            """]);
        await using var provider = Register("Ollama", handler).BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IChatClient>();
        var options = new ChatOptions
        {
            RawRepresentationFactory = _ => new ChatRequest
            {
                Messages = [new(OllamaSharp.Models.Chat.ChatRole.System, "Native instruction")]
            }
        };

        await client.GetResponseAsync([new(ChatRole.User, "Hello")], options);

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        var sent = body.RootElement.GetProperty("messages");
        Assert.Equal(2, sent.GetArrayLength());
        Assert.Equal("Native instruction", sent[0].GetProperty("content").GetString());
        Assert.Equal("Hello", sent[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task OllamaRejectsOrphanedToolResultsBeforeSendingARequest()
    {
        var handler = new RecordingHandler([]);
        await using var provider = Register("Ollama", handler).BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IChatClient>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync(
            [new(ChatRole.Tool, [new FunctionResultContent("missing-call", "result")])]));

        Assert.Empty(handler.Requests);
    }
}
