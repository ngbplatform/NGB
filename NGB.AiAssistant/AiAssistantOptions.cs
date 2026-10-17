namespace NGB.AiAssistant;

public sealed class AiAssistantOptions
{
    public const string SectionName = "AiAssistant";
    public const int MaximumSupportedInputBytes = 1024 * 1024;
    public const int MaximumSupportedContextBytes = 4 * 1024 * 1024;
    public const int MaximumSupportedMessages = 100;
    public const int MaximumSupportedToolCalls = 64;
    public const int MaximumSupportedTools = 250;
    public const int MaximumSupportedOutputTokens = 16 * 1024;
    public const int MaximumSupportedTimeoutSeconds = 600;
    public const int MaximumSupportedConcurrency = 64;
    public const int MaximumSupportedRequestsPerMinute = 10_000;

    public int MaxInputBytes { get; set; } = 32 * 1024;
    public int MaxMessages { get; set; } = 20;
    public int MaxMessageCharacters { get; set; } = 8000;
    public int MaxContextBytes { get; set; } = 64 * 1024;
    public int MaxToolResultBytes { get; set; } = 16 * 1024;
    public int MaxToolCalls { get; set; } = 12;
    public int MaxTools { get; set; } = 40;
    public int MaxOutputTokens { get; set; } = 2048;
    public int MaxOutputCharacters { get; set; } = 16 * 1024;
    public int RequestTimeoutSeconds { get; set; } = 180;
    public int ConcurrentRequests { get; set; } = 4;
    public int RequestsPerMinute { get; set; } = 30;

    public bool IsValid()
        => MaxInputBytes is > 0 and <= MaximumSupportedInputBytes
           && MaxMessages is > 0 and <= MaximumSupportedMessages
           && MaxMessageCharacters > 0
           && MaxMessageCharacters <= MaxInputBytes
           && MaxContextBytes >= MaxInputBytes
           && MaxContextBytes <= MaximumSupportedContextBytes
           && MaxToolResultBytes > 0
           && MaxToolResultBytes <= MaxContextBytes
           && MaxToolCalls is > 0 and <= MaximumSupportedToolCalls
           && MaxTools is > 0 and <= MaximumSupportedTools
           && MaxOutputTokens is > 0 and <= MaximumSupportedOutputTokens
           && MaxOutputCharacters is > 0 and <= MaximumSupportedContextBytes
           && RequestTimeoutSeconds is > 0 and <= MaximumSupportedTimeoutSeconds
           && ConcurrentRequests is > 0 and <= MaximumSupportedConcurrency
           && RequestsPerMinute is > 0 and <= MaximumSupportedRequestsPerMinute;
}
