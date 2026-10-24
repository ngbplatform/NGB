using Amazon;
using Amazon.BedrockRuntime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace NGB.AiAssistant.Bedrock;

public static class DependencyInjection
{
    /// <summary>Uses the AWS SDK credential chain, including workload roles. Credentials never come from the browser.</summary>
    public static IServiceCollection AddNgbBedrockChatClient(this IServiceCollection services,
        Action<BedrockAssistantOptions> configure)
    {
        services.AddOptions<BedrockAssistantOptions>().Configure(configure)
            .Validate(options => !string.IsNullOrWhiteSpace(options.Model)
                && RegionEndpoint.EnumerableAllRegions.Any(region => region.SystemName == options.Region),
                "Configure an AWS region and Bedrock model or inference profile.")
            .ValidateOnStart();

        services.TryAddScoped<IAmazonBedrockRuntime>(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<BedrockAssistantOptions>>().Value;
            return new AmazonBedrockRuntimeClient(new AmazonBedrockRuntimeConfig
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(settings.Region),
                MaxErrorRetry = 0
            });
        });

        services.AddScoped<IChatClient>(provider => provider.GetRequiredService<IAmazonBedrockRuntime>()
            .AsIChatClient(provider.GetRequiredService<IOptions<BedrockAssistantOptions>>().Value.Model));

        services.AddSingleton(serviceProvider =>
            new AiAssistantModelInfo("Bedrock", serviceProvider.GetRequiredService<IOptions<BedrockAssistantOptions>>().Value.Model));

        return services;
    }
}
