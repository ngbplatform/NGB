namespace NGB.Contracts.Metadata;

public sealed record PartMetadataDto(
    string PartCode,
    string Title,
    ListMetadataDto List,
    bool AllowAddRemoveRows = true,
    bool ReadOnlyWhenPosted = true);

public sealed record DocumentCapabilitiesDto(
    bool CanCreate = true,
    bool CanEditDraft = true,
    bool CanDeleteDraft = true,
    bool CanPost = true,
    bool CanUnpost = true,
    bool CanRepost = true,
    bool CanMarkForDeletion = true,
    bool SupportsActions = false);

public sealed record DocumentPresentationDto(
    string? DisplayName = null,
    bool HasNumber = true,
    bool ComputedDisplay = false,
    bool HideSystemFieldsInEditor = false,
    string? AmountField = null);

public sealed record DocumentTypeMetadataDto(
    string DocumentType,
    string DisplayName,
    EntityKind Kind,
    string? Icon = null,
    ListMetadataDto? List = null,
    FormMetadataDto? Form = null,
    IReadOnlyList<PartMetadataDto>? Parts = null,
    IReadOnlyList<ActionMetadataDto>? Actions = null,
    DocumentPresentationDto? Presentation = null,
    DocumentCapabilitiesDto? Capabilities = null);
