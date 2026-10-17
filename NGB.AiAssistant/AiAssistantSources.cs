namespace NGB.AiAssistant;

internal static class AiAssistantSources
{
    internal const int MaximumSources = 30;

    internal static bool IsValid(AiAssistantSource source)
        => source.Kind is "catalog" or "document" or "generalJournalEntry" or "report"
           && source.TypeCode is { Length: > 0 and <= 128 }
           && source.TypeCode.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')
           && (source.Kind == "report" || source.Id is not null && source.Id != Guid.Empty)
           && source.Title is { Length: > 0 and <= 256 }
           && !string.IsNullOrWhiteSpace(source.Title);
}
