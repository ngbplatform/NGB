using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

/// <summary>
/// Friendly validation errors for pm.property (Building/Unit variant).
/// </summary>
public sealed class PropertyValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null,
    Exception? innerException = null)
    : NgbValidationException(message, errorCode, context, innerException)
{
    public static PropertyValidationException KindRequired()
    {
        const string message = "Property type is required.";
        return new(
            message: message,
            errorCode: "pm.validation.property.kind_required",
            context: BuildFieldContext(
                field: "kind",
                message: message,
                extra: new Dictionary<string, object?> { ["catalogCode"] = "pm.property" }));
    }

    public static PropertyValidationException KindInvalid(string? kind)
    {
        const string message = "Property type must be Building or Unit.";
        return new(
            message: message,
            errorCode: "pm.validation.property.kind_invalid",
            context: BuildFieldContext(
                field: "kind",
                message: "Select Building or Unit.",
                extra: new Dictionary<string, object?>
                {
                    ["catalogCode"] = "pm.property",
                    ["kind"] = kind
                }));
    }

    public static PropertyValidationException KindImmutable(Guid catalogId, string? oldKind, string? newKind)
    {
        const string message = "Property type cannot be changed after the property is created.";
        return new(
            message: message,
            errorCode: "pm.validation.property.kind_immutable",
            context: BuildFieldContext(
                field: "kind",
                message: message,
                extra: new Dictionary<string, object?>
                {
                    ["catalogId"] = catalogId,
                    ["oldKind"] = oldKind,
                    ["newKind"] = newKind
                }));
    }

    public static PropertyValidationException BuildingCannotHaveParent(Guid parentId)
    {
        const string message = "Building cannot have a parent property.";
        return new(
            message: message,
            errorCode: "pm.validation.property.building.parent_not_allowed",
            context: BuildFieldContext(
                field: "parent_property_id",
                message: message,
                extra: new Dictionary<string, object?>
                {
                    ["kind"] = "Building",
                    ["parentPropertyId"] = parentId
                }));
    }

    public static PropertyValidationException BuildingCannotHaveUnitNo(string? unitNo)
    {
        const string message = "Unit number is not allowed for a building.";
        return new(
            message: message,
            errorCode: "pm.validation.property.building.unit_no_not_allowed",
            context: BuildFieldContext(
                field: "unit_no",
                message: message,
                extra: new Dictionary<string, object?>
                {
                    ["kind"] = "Building",
                    ["unitNo"] = unitNo
                }));
    }

    public static PropertyValidationException BuildingAddressRequired(string field)
    {
        var label = FieldLabel(field);
        var message = $"{label} is required.";
        return new(
            message: message,
            errorCode: "pm.validation.property.building.address_required",
            context: BuildFieldContext(
                field: field,
                message: message,
                extra: new Dictionary<string, object?>
                {
                    ["kind"] = "Building",
                    ["fieldLabel"] = label
                }));
    }

    public static PropertyValidationException UnitParentRequired()
    {
        const string message = "Parent building is required.";
        return new(
            message: message,
            errorCode: "pm.validation.property.unit.parent_required",
            context: BuildFieldContext(
                field: "parent_property_id",
                message: message,
                extra: new Dictionary<string, object?> { ["kind"] = "Unit" }));
    }

    public static PropertyValidationException UnitNoRequired()
    {
        const string message = "Unit number is required.";
        return new(
            message: message,
            errorCode: "pm.validation.property.unit.unit_no_required",
            context: BuildFieldContext(
                field: "unit_no",
                message: message,
                extra: new Dictionary<string, object?> { ["kind"] = "Unit" }));
    }

    public static PropertyValidationException UnitNoInvalid(string? unitNo)
    {
        const string message = "Unit number cannot start or end with spaces.";
        return new(
            message: message,
            errorCode: "pm.validation.property.unit.unit_no_invalid",
            context: BuildFieldContext(
                field: "unit_no",
                message: message,
                extra: new Dictionary<string, object?>
                {
                    ["kind"] = "Unit",
                    ["unitNo"] = unitNo
                }));
    }

    public static PropertyValidationException UnitAddressNotAllowed(string field)
    {
        var label = FieldLabel(field);
        var message = $"{label} is not allowed for a unit.";
        return new(
            message: message,
            errorCode: "pm.validation.property.unit.address_not_allowed",
            context: BuildFieldContext(
                field: field,
                message: message,
                extra: new Dictionary<string, object?>
                {
                    ["kind"] = "Unit",
                    ["fieldLabel"] = label
                }));
    }

    public static PropertyValidationException ParentNotFound(Guid parentId)
    {
        const string message = "Parent property was not found.";
        return new(
            message: message,
            errorCode: "pm.validation.property.unit.parent_not_found",
            context: BuildFieldContext(
                field: "parent_property_id",
                message: message,
                extra: new Dictionary<string, object?> { ["parentPropertyId"] = parentId }));
    }

    public static PropertyValidationException ParentWrongCatalog(Guid parentId, string? actualCatalogCode)
    {
        const string message = "Parent property is invalid.";
        return new(
            message: message,
            errorCode: "pm.validation.property.unit.parent_wrong_type",
            context: BuildFieldContext(
                field: "parent_property_id",
                message: message,
                extra: new Dictionary<string, object?>
                {
                    ["parentPropertyId"] = parentId,
                    ["actualCatalogCode"] = actualCatalogCode
                }));
    }

    public static PropertyValidationException ParentDeleted(Guid parentId)
    {
        const string message = "Parent property is marked for deletion.";
        return new(
            message: message,
            errorCode: "pm.validation.property.unit.parent_deleted",
            context: BuildFieldContext(
                field: "parent_property_id",
                message: message,
                extra: new Dictionary<string, object?> { ["parentPropertyId"] = parentId }));
    }

    public static PropertyValidationException ParentNotBuilding(Guid parentId, string? actualKind)
    {
        const string message = "Parent property must be a building.";
        return new(
            message: message,
            errorCode: "pm.validation.property.unit.parent_must_be_building",
            context: BuildFieldContext(
                field: "parent_property_id",
                message: message,
                extra: new Dictionary<string, object?>
                {
                    ["parentPropertyId"] = parentId,
                    ["actualKind"] = actualKind
                }));
    }

    public static PropertyValidationException CycleDetected(Guid catalogId, Guid parentId)
    {
        const string message = "Parent property creates a cycle.";
        return new(
            message: message,
            errorCode: "pm.validation.property.parent_cycle",
            context: BuildFieldContext(
                field: "parent_property_id",
                message: message,
                extra: new Dictionary<string, object?>
                {
                    ["catalogId"] = catalogId,
                    ["parentPropertyId"] = parentId
                }));
    }

    private static IReadOnlyDictionary<string, object?> BuildFieldContext(
        string field,
        string message,
        Dictionary<string, object?>? extra = null)
    {
        var ctx = extra ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        ctx["field"] = field;
        ctx["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [field] = [message]
        };
        return ctx;
    }

    private static string FieldLabel(string field)
        => field switch
        {
            "address_line1" => "Address Line 1",
            "address_line2" => "Address Line 2",
            "city" => "City",
            "state" => "State",
            "zip" => "Zip",
            "unit_no" => "Unit number",
            "parent_property_id" => "Parent building",
            "kind" => "Property type",
            _ => field
        };
}
