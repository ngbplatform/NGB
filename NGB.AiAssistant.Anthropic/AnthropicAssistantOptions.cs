namespace NGB.AiAssistant.Anthropic;

public sealed class AnthropicAssistantOptions
{
    public const string SectionName = "AiAssistant:Anthropic";

    public string Model { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}
