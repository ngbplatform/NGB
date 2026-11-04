using EquipmentInspection.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NGB.AiAssistant;
using NGB.AiAssistant.AspNetCore;
using NGB.Tools.Exceptions;
using Xunit;

namespace EquipmentInspection.IntegrationTests;

public sealed class AssistantConfigurationTests
{
    [Theory]
    [InlineData("OpenAI", "AiAssistant:OpenAI:ApiKey", "")]
    [InlineData("OpenAI", "AiAssistant:OpenAI:Model", "")]
    [InlineData("OpenAI", "AiAssistant:OpenAI:Endpoint", "http://provider.example.test/v1")]
    [InlineData("OpenAI", "AiAssistant:OpenAI:Endpoint", "https://user:example-fixture-key@provider.example.test/v1")]
    [InlineData("Anthropic", "AiAssistant:Anthropic:ApiKey", "")]
    [InlineData("Anthropic", "AiAssistant:Anthropic:Model", "")]
    [InlineData("Bedrock", "AiAssistant:Bedrock:Region", "unknown-region")]
    [InlineData("Bedrock", "AiAssistant:Bedrock:Model", "")]
    [InlineData("Ollama", "AiAssistant:Ollama:Endpoint", "http://provider.example.test:11434")]
    [InlineData("Ollama", "AiAssistant:Ollama:Model", "remote-cloud")]
    [InlineData("Ollama", "AiAssistant:Ollama:ContextLength", "131073")]
    [InlineData("Ollama", "AiAssistant:MaxToolCalls", "0")]
    [InlineData("Ollama", "AiAssistant:RequestTimeoutSeconds", "601")]
    [InlineData("Ollama", "AiAssistant:MaxOutputTokens", "16385")]
    public void Invalid_selected_provider_and_limits_fail_startup_validation_without_exposing_secrets(string provider, string key, string value)
    {
        var values = AssistantProviderFixture.Configuration(provider);
        values[key] = value;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEquipmentAssistant(AssistantProviderFixture.Bind(values));
        using var container = services.BuildServiceProvider();

        var failure = Assert.Throws<OptionsValidationException>(() => container.GetRequiredService<IStartupValidator>().Validate());

        Assert.DoesNotContain(AssistantProviderFixture.ApiKey, failure.Message);
    }

    [Theory]
    [InlineData("OpenAI")]
    [InlineData("Anthropic")]
    [InlineData("Bedrock")]
    [InlineData("Ollama")]
    public async Task Valid_selected_provider_cancels_the_actual_adapter_transport(string provider)
    {
        var fixture = new AssistantProviderFixture(provider) { Wait = true };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEquipmentAssistant(AssistantProviderFixture.Bind(AssistantProviderFixture.Configuration(provider)));
        fixture.Register(services);
        await using var container = services.BuildServiceProvider();
        container.GetRequiredService<IStartupValidator>().Validate();
        Assert.Equal(new AiAssistantModelInfo(provider, AssistantProviderFixture.Model), container.GetRequiredService<AiAssistantModelInfo>());
        await using var scope = container.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IChatClient>();
        using var cancellation = new CancellationTokenSource();
        var response = client.GetStreamingResponseAsync([new(ChatRole.User, "Read the equipment record.")],
                new ChatOptions { MaxOutputTokens = 128 }, cancellation.Token)
            .ToChatResponseAsync(cancellationToken: cancellation.Token);
        await fixture.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await response);
        await fixture.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(fixture.Requests);
    }

    [Fact]
    public async Task Enabled_assistant_requires_an_explicit_supported_provider_and_a_mapped_Mcp_endpoint()
    {
        var values = AssistantProviderFixture.Configuration("unknown-provider");
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddEquipmentAssistant(AssistantProviderFixture.Bind(values)));

        var missingMcp = AssistantProviderFixture.Configuration("Ollama");
        missingMcp["FeatureManagement:Mcp"] = "false";
        Assert.Throws<NgbConfigurationViolationException>(() =>
            new ServiceCollection().AddEquipmentAssistant(AssistantProviderFixture.Bind(missingMcp)));

        var builder = WebApplication.CreateBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddConfiguration(AssistantProviderFixture.Bind(AssistantProviderFixture.Configuration("Ollama")));
        builder.Services.AddEquipmentAssistant(builder.Configuration);
        await using var app = builder.Build();
        Assert.Throws<InvalidOperationException>(() => app.MapNgbAiAssistant());
    }

    [Fact]
    public void Disabled_assistant_does_not_require_or_resolve_any_provider_credentials()
    {
        var values = AssistantProviderFixture.Configuration("unsupported-and-disabled");
        values["FeatureManagement:AiAssistant"] = "false";
        var services = new ServiceCollection();
        services.AddEquipmentAssistant(AssistantProviderFixture.Bind(values));
        using var container = services.BuildServiceProvider();

        Assert.Null(container.GetService<AiAssistantModelInfo>());
        Assert.Null(container.GetService<IChatClient>());
        Assert.Null(container.GetService<AiAssistantRunner>());
    }
}
