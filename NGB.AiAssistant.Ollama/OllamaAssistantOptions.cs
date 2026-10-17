namespace NGB.AiAssistant.Ollama;

public sealed class OllamaAssistantOptions
{
    public const string SectionName = "AiAssistant:Ollama";
    public const int MaximumContextLength = 128 * 1024;

    public string Endpoint { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "qwen3.5:4b";
    public int ContextLength { get; set; } = 32 * 1024;
}
