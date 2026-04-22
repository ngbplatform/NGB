namespace NGB.Contracts.Metadata;

public enum NgbActionKind
{
    Primary = 1,
    Secondary = 2,
    Dangerous = 3
}

public sealed record ActionMetadataDto(
    string Code,
    string Label,
    NgbActionKind Kind = NgbActionKind.Primary,
    bool RequiresConfirm = false,
    IReadOnlyList<DocumentStatus>? VisibleWhenStatusIn = null);
