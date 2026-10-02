namespace NGB.Runtime.Features;

/// <summary>A deployment-wide feature. All features are disabled unless explicitly configured.</summary>
public sealed record NgbFeatureDefinition(string Code, string DisplayName, string Group);
