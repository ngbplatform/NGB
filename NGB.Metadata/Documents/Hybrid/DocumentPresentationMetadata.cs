namespace NGB.Metadata.Documents.Hybrid;

/// <summary>
/// Optional presentation info for enrichment / UI. Not used by core posting logic.
/// </summary>
public sealed record DocumentPresentationMetadata(
    string? DisplayName = null,
    bool HasNumber = true,
    bool ComputedDisplay = false,
    bool HideSystemFieldsInEditor = false,
    string? AmountField = null);
