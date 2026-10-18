namespace NGB.AiAssistant.Ollama;

public sealed class OllamaAssistantOptions
{
    public const string SectionName = "AiAssistant:Ollama";
    public const int MaximumContextLength = 128 * 1024;

    public string Endpoint { get; set; } = "http://localhost:11434";

    /// <summary>Explicitly permits HTTP beyond loopback. Disabled by default; use only on a trusted network.</summary>
    public bool AllowInsecureHttp { get; set; }

    public string Model { get; set; } = "qwen3.5:9b";
    public int ContextLength { get; set; } = 32 * 1024;
}
