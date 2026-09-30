using Microsoft.Extensions.Options;

namespace NGB.Runtime.Security;

public sealed class NgbSecurityCacheOptions
{
    public static TimeSpan DefaultPermissionSnapshotTtl { get; } = TimeSpan.FromSeconds(45);

    public static TimeSpan DefaultPermissionAwareTtl { get; } = TimeSpan.FromMinutes(5);

    public const int DefaultMaxEntries = 20_000;

    public TimeSpan PermissionSnapshotTtl { get; init; } = DefaultPermissionSnapshotTtl;

    public TimeSpan MainMenuTtl { get; init; } = DefaultPermissionAwareTtl;

    public TimeSpan CatalogMetadataTtl { get; init; } = DefaultPermissionAwareTtl;

    public TimeSpan DocumentMetadataTtl { get; init; } = DefaultPermissionAwareTtl;

    public TimeSpan ReportDefinitionsTtl { get; init; } = DefaultPermissionAwareTtl;

    public int MaxEntries { get; init; } = DefaultMaxEntries;
}

public sealed class NgbSecurityCacheOptionsValidator : IValidateOptions<NgbSecurityCacheOptions>
{
    private static readonly TimeSpan MinimumTtl = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumTtl = TimeSpan.FromHours(1);

    public ValidateOptionsResult Validate(string? name, NgbSecurityCacheOptions options)
    {
        var failures = new List<string>();

        ValidateTtl(options.PermissionSnapshotTtl, nameof(options.PermissionSnapshotTtl), failures);
        ValidateTtl(options.MainMenuTtl, nameof(options.MainMenuTtl), failures);
        ValidateTtl(options.CatalogMetadataTtl, nameof(options.CatalogMetadataTtl), failures);
        ValidateTtl(options.DocumentMetadataTtl, nameof(options.DocumentMetadataTtl), failures);
        ValidateTtl(options.ReportDefinitionsTtl, nameof(options.ReportDefinitionsTtl), failures);

        if (options.MaxEntries is < 100 or > 200_000)
            failures.Add($"{nameof(NgbSecurityCacheOptions)}.{nameof(options.MaxEntries)} must be between 100 and 200000.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateTtl(TimeSpan value, string optionName, ICollection<string> failures)
    {
        if (value < MinimumTtl || value > MaximumTtl)
            failures.Add($"{nameof(NgbSecurityCacheOptions)}.{optionName} must be between {MinimumTtl} and {MaximumTtl}.");
    }
}
