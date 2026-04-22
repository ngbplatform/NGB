using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

/// <summary>
/// Friendly validation errors for the pm.property bulk-create-units wizard.
/// </summary>
public sealed class PropertyBulkCreateUnitsValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null,
    Exception? innerException = null)
    : NgbValidationException(message, errorCode, context, innerException)
{
    public static PropertyBulkCreateUnitsValidationException BuildingRequired()
    {
        const string message = "Building is required.";
        return new(
            message,
            "pm.validation.property.bulk_create_units.building_required",
            BuildFieldContext("buildingId", message));
    }

    public static PropertyBulkCreateUnitsValidationException StepMustBePositive(int step)
    {
        const string message = "Step must be greater than 0.";
        return new(
            message,
            "pm.validation.property.bulk_create_units.step_invalid",
            BuildFieldContext(
                "step",
                message,
                new Dictionary<string, object?> { ["step"] = step }));
    }

    public static PropertyBulkCreateUnitsValidationException RangeMustBePositive(int fromInclusive, int toInclusive)
    {
        const string message = "From and To must be greater than 0.";
        return new(
            message,
            "pm.validation.property.bulk_create_units.range_positive_required",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["fromInclusive"] = fromInclusive,
                ["toInclusive"] = toInclusive,
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["fromInclusive"] = fromInclusive <= 0 ? ["From must be greater than 0."] : [],
                    ["toInclusive"] = toInclusive <= 0 ? ["To must be greater than 0."] : []
                }
            }.WithoutEmptyErrors());
    }

    public static PropertyBulkCreateUnitsValidationException RangeInvalid(int fromInclusive, int toInclusive)
    {
        const string message = "From must be less than or equal to To.";
        return new(
            message,
            "pm.validation.property.bulk_create_units.range_invalid",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["fromInclusive"] = fromInclusive,
                ["toInclusive"] = toInclusive,
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["fromInclusive"] = [message],
                    ["toInclusive"] = [message]
                }
            });
    }

    public static PropertyBulkCreateUnitsValidationException UnitNoFormatRequired()
    {
        const string message = "Unit number format is required.";
        return new(
            message,
            "pm.validation.property.bulk_create_units.unit_no_format_required",
            BuildFieldContext("unitNoFormat", message));
    }

    public static PropertyBulkCreateUnitsValidationException UnitNoFormatMustIncludeNumberPlaceholder(
        string? unitNoFormat)
    {
        const string message = "Unit number format must include {0}.";
        return new(
            message,
            "pm.validation.property.bulk_create_units.unit_no_format_missing_number_placeholder",
            BuildFieldContext(
                "unitNoFormat",
                message,
                new Dictionary<string, object?> { ["unitNoFormat"] = unitNoFormat }));
    }

    public static PropertyBulkCreateUnitsValidationException FloorSizeMustBePositive(int floorSize)
    {
        const string message = "Floor size must be greater than 0.";
        return new(
            message,
            "pm.validation.property.bulk_create_units.floor_size_invalid",
            BuildFieldContext(
                "floorSize",
                message,
                new Dictionary<string, object?> { ["floorSize"] = floorSize }));
    }

    public static PropertyBulkCreateUnitsValidationException TooManyUnitsRequested(int count, int max)
    {
        var message = $"You can create up to {max:N0} units in one run.";
        return new(
            message,
            "pm.validation.property.bulk_create_units.too_many_units",
            BuildFieldContext(
                "toInclusive",
                message,
                new Dictionary<string, object?>
                {
                    ["count"] = count,
                    ["max"] = max,
                    ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        ["toInclusive"] = [message],
                        ["fromInclusive"] = [message]
                    }
                }.WithoutEmptyErrors()));
    }

    public static PropertyBulkCreateUnitsValidationException UnitNoFormatInvalid(string unitNoFormat, Exception inner)
    {
        return new(
            "Unit number format is invalid.",
            "pm.validation.property.bulk_create_units.unit_no_format_invalid",
            BuildFieldContext(
                "unitNoFormat",
                "Use a format like {0}, {0:0000}, or {1}-{0:000}.",
                new Dictionary<string, object?> { ["unitNoFormat"] = unitNoFormat }),
            inner);
    }

    public static PropertyBulkCreateUnitsValidationException GeneratedUnitNoEmpty(string unitNoFormat)
    {
        return new(
            "Unit number format produced an empty unit number.",
            "pm.validation.property.bulk_create_units.generated_unit_no_empty",
            BuildFieldContext(
                "unitNoFormat",
                "Choose a format that produces a unit number, for example {0:0000}.",
                new Dictionary<string, object?> { ["unitNoFormat"] = unitNoFormat }));
    }

    private static IReadOnlyDictionary<string, object?> BuildFieldContext(
        string field,
        string fieldMessage,
        Dictionary<string, object?>? extra = null)
    {
        var ctx = extra ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        ctx["field"] = field;
        if (!ctx.ContainsKey("errors"))
        {
            ctx["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = [fieldMessage]
            };
        }

        return ctx;
    }
}

internal static class PropertyBulkCreateUnitsValidationContextExtensions
{
    public static Dictionary<string, object?> WithoutEmptyErrors(this Dictionary<string, object?> context)
    {
        if (!context.TryGetValue("errors", out var errorsObj) || errorsObj is not Dictionary<string, string[]> errors)
            return context;

        var filtered = errors
            .Where(x => x.Value.Length > 0)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

        context["errors"] = filtered;
        return context;
    }
}
