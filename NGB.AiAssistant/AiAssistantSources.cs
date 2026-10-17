using NGB.Contracts.Common;

namespace NGB.AiAssistant;

internal static class AiAssistantSources
{
    internal const int MaximumSources = 30;

    internal static bool IsValid(AiAssistantSource source)
        => NgbSourceKinds.IsSupported(source.Kind)
           && source.TypeCode is { Length: > 0 and <= 128 }
           && source.TypeCode.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')
           && (source.Kind == NgbSourceKinds.Report || source.Id is not null && source.Id != Guid.Empty)
           && source.Title is { Length: > 0 and <= 256 }
           && !string.IsNullOrWhiteSpace(source.Title);
}
