using EquipmentInspection.Definitions;
using NGB.Definitions.Catalogs.Validation;
using NGB.Tools.Exceptions;

namespace EquipmentInspection.Runtime;

public sealed class EquipmentCategoryValidator : ICatalogUpsertValidator
{
    public string TypeCode => EquipmentCategoryDefinitions.TypeCode;

    public Task ValidateUpsertAsync(CatalogUpsertValidationContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ValidateField(context, "code", 64);
        ValidateField(context, "name", 200);
        return Task.CompletedTask;
    }

    private static void ValidateField(CatalogUpsertValidationContext context, string field, int maximumLength)
    {
        if (!context.Fields.TryGetValue(field, out var value) || value is not string text
            || string.IsNullOrWhiteSpace(text) || text.Length > maximumLength)
        {
            throw new NgbArgumentInvalidException(field, $"Use nonempty text with at most {maximumLength} characters.");
        }
    }
}
