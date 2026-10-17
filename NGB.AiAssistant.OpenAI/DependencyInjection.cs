using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NGB.Tools.Http;
using OpenAI;
using OpenAI.Chat;

namespace NGB.AiAssistant.OpenAI;

public static class DependencyInjection
{
    public const string HttpClientName = "NGB.AiAssistant.OpenAI";

    /// <summary>Uses Chat Completions for OpenAI and compatible providers with tool calling and streaming.</summary>
    public static IServiceCollection AddNgbOpenAiChatClient(this IServiceCollection services,
        Action<OpenAiAssistantOptions> configure)
    {
        services.AddOptions<OpenAiAssistantOptions>().Configure(configure)
            .Validate(options => HttpEndpointUri.IsSecureAbsolute(options.Endpoint)
                && !string.IsNullOrWhiteSpace(options.Model) && !string.IsNullOrWhiteSpace(options.ApiKey),
                "Configure an HTTPS endpoint, model and API key for the selected OpenAI-compatible provider.")
            .ValidateOnStart();

        services.AddHttpClient(HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });

        services.AddScoped<IChatClient>(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<OpenAiAssistantOptions>>().Value;
            var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            return new ChatClient(settings.Model, new ApiKeyCredential(settings.ApiKey), new OpenAIClientOptions
            {
                Endpoint = new Uri(settings.Endpoint),
                Transport = new HttpClientPipelineTransport(http),
                RetryPolicy = new ClientRetryPolicy(0)
            }).AsIChatClient();
        });

        services.AddSingleton(serviceProvider =>
            new AiAssistantModelInfo("OpenAI", serviceProvider.GetRequiredService<IOptions<OpenAiAssistantOptions>>().Value.Model));

        return services;
    }
}
