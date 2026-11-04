using EquipmentInspection.Definitions;
using NGB.Definitions.Catalogs.Validation;
using NGB.Tools.Exceptions;

namespace EquipmentInspection.Runtime;

public sealed class EquipmentNameValidator : ICatalogUpsertValidator
{
    public string TypeCode => EquipmentDefinitions.EquipmentCode;

    public Task ValidateUpsertAsync(CatalogUpsertValidationContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!context.Fields.TryGetValue("display", out var value)
            || value is not string name || string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl))
        {
            throw new NgbArgumentInvalidException("display", "Use a nonempty, single-line equipment name without control characters.");
        }

        return Task.CompletedTask;
    }
}
