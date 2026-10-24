namespace NGB.AiAssistant.Bedrock;

public sealed class BedrockAssistantOptions
{
    public const string SectionName = "AiAssistant:Bedrock";

    public string Region { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}
