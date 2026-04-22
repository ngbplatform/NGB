namespace NGB.Metadata.Documents.Relationships;

public sealed record DocumentRelationshipsPhysicalSchemaHealth(
    string TableName,
    bool Exists,
    IReadOnlyList<string> MissingColumns,
    IReadOnlyList<string> MissingIndexes,
    IReadOnlyList<string> MissingConstraints,
    bool HasDraftGuardTrigger,
    bool HasDraftGuardFunction,
    bool HasMirroringComputeFunction,
    bool HasMirroringSyncFunction,
    bool HasMirroringInstallerFunction,
    IReadOnlyList<string> MissingMirroredTriggerBindings
);
