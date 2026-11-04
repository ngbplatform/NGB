using NGB.AiAssistant.Anthropic;
using NGB.AiAssistant.AspNetCore;
using NGB.AiAssistant.Bedrock;
using NGB.AiAssistant.Ollama;
using NGB.AiAssistant.OpenAI;

namespace EquipmentInspection.Api;

public static class EquipmentAssistant
{
    public static IServiceCollection AddEquipmentAssistant(this IServiceCollection services, IConfiguration configuration)
    {
        return services.AddNgbAiAssistant(configuration, providerServices =>
        {
            switch (configuration["AiAssistant:Provider"])
            {
                case "OpenAI":
                    providerServices.AddNgbOpenAiChatClient(configuration.GetSection(OpenAiAssistantOptions.SectionName).Bind);
                    break;
                case "Anthropic":
                    providerServices.AddNgbAnthropicChatClient(configuration.GetSection(AnthropicAssistantOptions.SectionName).Bind);
                    break;
                case "Bedrock":
                    providerServices.AddNgbBedrockChatClient(configuration.GetSection(BedrockAssistantOptions.SectionName).Bind);
                    break;
                case "Ollama":
                    providerServices.AddNgbOllamaChatClient(configuration.GetSection(OllamaAssistantOptions.SectionName).Bind);
                    break;
                default:
                    throw new InvalidOperationException("Select an explicit supported Assistant provider.");
            }
        });
    }
}
