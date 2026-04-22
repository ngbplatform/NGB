namespace NGB.Contracts.Metadata;

public sealed record FormRowDto(IReadOnlyList<FieldMetadataDto> Fields);

public sealed record FormSectionDto(string Title, IReadOnlyList<FormRowDto> Rows);

public sealed record FormMetadataDto(IReadOnlyList<FormSectionDto> Sections);
