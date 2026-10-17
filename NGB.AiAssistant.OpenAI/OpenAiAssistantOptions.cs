namespace NGB.AiAssistant.OpenAI;

public sealed class OpenAiAssistantOptions
{
    public const string SectionName = "AiAssistant:OpenAI";

    public string Endpoint { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}
