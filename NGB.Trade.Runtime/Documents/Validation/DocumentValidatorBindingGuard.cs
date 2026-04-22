using NGB.Core.Documents;
using NGB.Tools.Exceptions;

namespace NGB.Trade.Runtime.Documents.Validation;

internal static class DocumentValidatorBindingGuard
{
    public static void EnsureExpectedType(
        DocumentRecord documentForUpdate,
        string expectedTypeCode,
        string validatorName)
    {
        if (string.Equals(documentForUpdate.TypeCode, expectedTypeCode, StringComparison.OrdinalIgnoreCase))
            return;

        throw new NgbConfigurationViolationException(
            $"{validatorName} is bound to '{expectedTypeCode}', but was invoked for '{documentForUpdate.TypeCode}'.",
            context: new Dictionary<string, object?>
            {
                ["expectedTypeCode"] = expectedTypeCode,
                ["actualTypeCode"] = documentForUpdate.TypeCode,
                ["documentId"] = documentForUpdate.Id,
                ["validator"] = validatorName
            });
    }
}
