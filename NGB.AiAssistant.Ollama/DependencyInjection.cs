using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models;

namespace NGB.AiAssistant.Ollama;

public static class DependencyInjection
{
    public const string HttpClientName = "NGB.AiAssistant.Ollama";

    public static IServiceCollection AddNgbOllamaChatClient(this IServiceCollection services,
        Action<OllamaAssistantOptions> configure)
    {
        services.AddOptions<OllamaAssistantOptions>().Configure(configure)
            .Validate(options => AiProviderEndpoint.IsValid(options.Endpoint, allowLocalHttp: true)
                && !string.IsNullOrWhiteSpace(options.Model)
                && !options.Model.Contains("cloud", StringComparison.OrdinalIgnoreCase)
                && options.ContextLength is > 0 and <= OllamaAssistantOptions.MaximumContextLength,
                "Configure a local Ollama model, valid endpoint and context length.")
            .ValidateOnStart();

        services.AddHttpClient(HttpClientName, (provider, client) =>
        {
            client.BaseAddress = new Uri(provider.GetRequiredService<IOptions<OllamaAssistantOptions>>().Value.Endpoint);
            client.Timeout = Timeout.InfiniteTimeSpan;
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });

        services.AddScoped<IChatClient>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<OllamaAssistantOptions>>().Value;
            var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new ConfiguredOllamaClient(new OllamaApiClient(http, options.Model), options);
        });

        services.AddSingleton(serviceProvider =>
            new AiAssistantModelInfo("Ollama", serviceProvider.GetRequiredService<IOptions<OllamaAssistantOptions>>().Value.Model));

        return services;
    }

    private sealed class ConfiguredOllamaClient(
        IChatClient inner,
        OllamaAssistantOptions settings)
        : DelegatingChatClient(inner)
    {
        public override Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => base.GetResponseAsync(messages, Configure(options), cancellationToken);

        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => base.GetStreamingResponseAsync(messages, Configure(options), cancellationToken);

        private ChatOptions Configure(ChatOptions? options)
        {
            var configured = options?.Clone() ?? new ChatOptions();
            configured.ModelId = settings.Model;
            configured.AddOllamaOption(OllamaOption.NumCtx, settings.ContextLength);
            configured.AddOllamaOption(OllamaOption.Think, false);
            return configured;
        }
    }
}
