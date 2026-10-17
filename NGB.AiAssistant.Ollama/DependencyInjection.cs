using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NGB.Tools.Http;
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
            .Validate(options => IsValidEndpoint(options.Endpoint, options.AllowInsecureHttp),
                "Configure an HTTPS or loopback HTTP Ollama endpoint, or explicitly enable AiAssistant:Ollama:AllowInsecureHttp. "
                + "The endpoint must not contain credentials, a query or a fragment.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Model)
                && !options.Model.Contains("cloud", StringComparison.OrdinalIgnoreCase)
                && options.ContextLength is > 0 and <= OllamaAssistantOptions.MaximumContextLength,
                "Configure a local Ollama model and valid context length.")
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

    private static bool IsValidEndpoint(string value, bool allowInsecureHttp)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && HttpEndpointUri.IsAbsoluteHttp(uri)
            && (uri.Scheme == Uri.UriSchemeHttps
                || uri.IsLoopback
                || allowInsecureHttp);

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
