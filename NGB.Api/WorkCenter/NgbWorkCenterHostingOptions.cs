using Microsoft.Extensions.Options;

namespace NGB.Api.WorkCenter;

/// <summary>
/// API-host lifecycle policy for polling and draining the Work Center outbox.
/// </summary>
public sealed class NgbWorkCenterHostingOptions
{
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan MaintenanceInterval { get; init; } = TimeSpan.FromHours(6);

    public int ProjectionBatchSize { get; init; } = 25;

    /// <summary>
    /// Bounds one drain cycle so a continuously busy outbox cannot starve
    /// maintenance, health work, or the host scheduler.
    /// </summary>
    public int MaximumProjectionBatchesPerPoll { get; init; } = 20;
}

internal sealed class NgbWorkCenterHostingOptionsValidator : IValidateOptions<NgbWorkCenterHostingOptions>
{
    public ValidateOptionsResult Validate(string? name, NgbWorkCenterHostingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        ValidateDuration(options.PollInterval, TimeSpan.FromMilliseconds(250), TimeSpan.FromMinutes(1), nameof(options.PollInterval), failures);
        ValidateDuration(options.MaintenanceInterval, TimeSpan.FromMinutes(1), TimeSpan.FromDays(7), nameof(options.MaintenanceInterval), failures);
        ValidateRange(options.ProjectionBatchSize, 1, 100, nameof(options.ProjectionBatchSize), failures);
        ValidateRange(
            options.MaximumProjectionBatchesPerPoll,
            1,
            1_000,
            nameof(options.MaximumProjectionBatchesPerPoll),
            failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateDuration(
        TimeSpan value,
        TimeSpan minimum,
        TimeSpan maximum,
        string optionName,
        ICollection<string> failures)
    {
        if (value < minimum || value > maximum)
            failures.Add($"{nameof(NgbWorkCenterHostingOptions)}.{optionName} must be between {minimum} and {maximum}.");
    }

    private static void ValidateRange(
        int value,
        int minimum,
        int maximum,
        string optionName,
        ICollection<string> failures)
    {
        if (value < minimum || value > maximum)
            failures.Add($"{nameof(NgbWorkCenterHostingOptions)}.{optionName} must be between {minimum} and {maximum}.");
    }
}
