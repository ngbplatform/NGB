using Microsoft.Extensions.Options;

namespace NGB.Runtime.WorkCenter;

/// <summary>
/// Runtime retention and maintenance policy for the Work Center projection.
/// Host lifecycle and polling settings intentionally live in the API adapter.
/// </summary>
public sealed class NgbWorkCenterOptions
{
    public const string ConfigurationSection = "Ngb:WorkCenter";

    public TimeSpan DocumentActionExecutionRetention { get; init; } = TimeSpan.FromDays(90);

    public TimeSpan TerminalTaskRetention { get; init; } = TimeSpan.FromDays(180);

    public TimeSpan NotificationDeliveryRetention { get; init; } = TimeSpan.FromDays(30);

    public TimeSpan OutboxRetention { get; init; } = TimeSpan.FromDays(30);

    public int MaintenanceBatchSize { get; init; } = 1_000;

    public int MaximumMaintenanceBatchesPerRun { get; init; } = 10;

    /// <summary>
    /// Maximum number of independent outbox subjects projected concurrently.
    /// Events for the same subject always remain sequential.
    /// </summary>
    public int ProjectionParallelism { get; init; } = 4;
}

internal sealed class NgbWorkCenterOptionsValidator : IValidateOptions<NgbWorkCenterOptions>
{
    public ValidateOptionsResult Validate(string? name, NgbWorkCenterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        ValidateDuration(options.DocumentActionExecutionRetention, TimeSpan.FromDays(1), TimeSpan.FromDays(3650), nameof(options.DocumentActionExecutionRetention), failures);
        ValidateDuration(options.TerminalTaskRetention, TimeSpan.FromDays(1), TimeSpan.FromDays(3650), nameof(options.TerminalTaskRetention), failures);
        ValidateDuration(options.NotificationDeliveryRetention, TimeSpan.FromDays(1), TimeSpan.FromDays(3650), nameof(options.NotificationDeliveryRetention), failures);
        ValidateDuration(options.OutboxRetention, TimeSpan.FromDays(1), TimeSpan.FromDays(3650), nameof(options.OutboxRetention), failures);
        ValidateRange(options.MaintenanceBatchSize, 1, 10_000, nameof(options.MaintenanceBatchSize), failures);
        ValidateRange(options.MaximumMaintenanceBatchesPerRun, 1, 1_000, nameof(options.MaximumMaintenanceBatchesPerRun), failures);
        ValidateRange(options.ProjectionParallelism, 1, 16, nameof(options.ProjectionParallelism), failures);

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
            failures.Add($"{nameof(NgbWorkCenterOptions)}.{optionName} must be between {minimum} and {maximum}.");
    }

    private static void ValidateRange(
        int value,
        int minimum,
        int maximum,
        string optionName,
        ICollection<string> failures)
    {
        if (value < minimum || value > maximum)
            failures.Add($"{nameof(NgbWorkCenterOptions)}.{optionName} must be between {minimum} and {maximum}.");
    }
}
