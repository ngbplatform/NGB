using global::Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace NGB.AiAssistant.Anthropic;

public static class DependencyInjection
{
    public const string HttpClientName = "NGB.AiAssistant.Anthropic";

    public static IServiceCollection AddNgbAnthropicChatClient(this IServiceCollection services,
        Action<AnthropicAssistantOptions> configure)
    {
        services.AddOptions<AnthropicAssistantOptions>().Configure(configure)
            .Validate(options => !string.IsNullOrWhiteSpace(options.Model) && !string.IsNullOrWhiteSpace(options.ApiKey),
                "Configure an Anthropic model and API key.")
            .ValidateOnStart();

        services.AddHttpClient(HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });

        services.AddScoped<IChatClient>(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<AnthropicAssistantOptions>>().Value;
            var client = new AnthropicClient
            {
                ApiKey = settings.ApiKey,
                BaseUrl = "https://api.anthropic.com",
                HttpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
                MaxRetries = 0
            };

            return client.AsIChatClient(settings.Model);
        });

        services.AddSingleton(serviceProvider =>
            new AiAssistantModelInfo("Anthropic", serviceProvider.GetRequiredService<IOptions<AnthropicAssistantOptions>>().Value.Model));

        return services;
    }
}
