using Microsoft.Extensions.Options;

namespace NGB.Runtime.WorkCenter;

/// <summary>
/// Operational limits and retention policy for the Work Center projection.
/// Bind from the <c>Ngb:WorkCenter</c> configuration section in API hosts.
/// </summary>
public sealed class NgbWorkCenterOptions
{
    public const string ConfigurationSection = "Ngb:WorkCenter";

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan MaintenanceInterval { get; init; } = TimeSpan.FromHours(6);

    public TimeSpan DocumentActionExecutionRetention { get; init; } = TimeSpan.FromDays(90);

    public TimeSpan TerminalTaskRetention { get; init; } = TimeSpan.FromDays(180);

    public TimeSpan NotificationDeliveryRetention { get; init; } = TimeSpan.FromDays(30);

    public TimeSpan OutboxRetention { get; init; } = TimeSpan.FromDays(30);

    public int ProjectionBatchSize { get; init; } = 25;

    public int MaintenanceBatchSize { get; init; } = 1_000;

    public int MaximumMaintenanceBatchesPerRun { get; init; } = 10;
}

internal sealed class NgbWorkCenterOptionsValidator : IValidateOptions<NgbWorkCenterOptions>
{
    public ValidateOptionsResult Validate(string? name, NgbWorkCenterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        ValidateDuration(options.PollInterval, TimeSpan.FromMilliseconds(250), TimeSpan.FromMinutes(1), nameof(options.PollInterval), failures);
        ValidateDuration(options.MaintenanceInterval, TimeSpan.FromMinutes(1), TimeSpan.FromDays(7), nameof(options.MaintenanceInterval), failures);
        ValidateDuration(options.DocumentActionExecutionRetention, TimeSpan.FromDays(1), TimeSpan.FromDays(3650), nameof(options.DocumentActionExecutionRetention), failures);
        ValidateDuration(options.TerminalTaskRetention, TimeSpan.FromDays(1), TimeSpan.FromDays(3650), nameof(options.TerminalTaskRetention), failures);
        ValidateDuration(options.NotificationDeliveryRetention, TimeSpan.FromDays(1), TimeSpan.FromDays(3650), nameof(options.NotificationDeliveryRetention), failures);
        ValidateDuration(options.OutboxRetention, TimeSpan.FromDays(1), TimeSpan.FromDays(3650), nameof(options.OutboxRetention), failures);
        ValidateRange(options.ProjectionBatchSize, 1, 100, nameof(options.ProjectionBatchSize), failures);
        ValidateRange(options.MaintenanceBatchSize, 1, 10_000, nameof(options.MaintenanceBatchSize), failures);
        ValidateRange(options.MaximumMaintenanceBatchesPerRun, 1, 1_000, nameof(options.MaximumMaintenanceBatchesPerRun), failures);

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
