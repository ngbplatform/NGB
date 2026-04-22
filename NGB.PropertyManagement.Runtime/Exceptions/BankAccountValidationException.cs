using NGB.Accounting.Accounts;
using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class BankAccountValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbValidationException(message, errorCode, context)
{
    public static BankAccountValidationException Last4Invalid(string? last4, Guid? bankAccountId = null)
    {
        const string message = "Last 4 digits must contain exactly 4 digits.";
        return new(
            message,
            "pm.validation.bank_account.last4_invalid",
            BuildContext(
                bankAccountId: bankAccountId,
                last4: last4,
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["last4"] = [message]
                }));
    }

    public static BankAccountValidationException GlAccountRequired(Guid? bankAccountId = null)
    {
        const string message = "GL account is required.";
        return new(
            message,
            "pm.validation.bank_account.gl_account_required",
            BuildContext(
                bankAccountId: bankAccountId,
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["gl_account_id"] = [message]
                }));
    }

    public static BankAccountValidationException GlAccountNotFound(Guid glAccountId, Guid? bankAccountId = null)
    {
        const string message = "Selected GL account was not found.";
        return new(
            message,
            "pm.validation.bank_account.gl_account_not_found",
            BuildContext(
                bankAccountId: bankAccountId,
                glAccountId: glAccountId,
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["gl_account_id"] = [message]
                }));
    }

    public static BankAccountValidationException GlAccountDeleted(Guid glAccountId, Guid? bankAccountId = null)
    {
        const string message = "Selected GL account is marked for deletion.";
        return new(
            message,
            "pm.validation.bank_account.gl_account_deleted",
            BuildContext(
                bankAccountId: bankAccountId,
                glAccountId: glAccountId,
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["gl_account_id"] = [message]
                }));
    }

    public static BankAccountValidationException GlAccountInactive(Guid glAccountId, Guid? bankAccountId = null)
    {
        const string message = "Selected GL account is inactive.";
        return new(
            message,
            "pm.validation.bank_account.gl_account_inactive",
            BuildContext(
                bankAccountId: bankAccountId,
                glAccountId: glAccountId,
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["gl_account_id"] = [message]
                }));
    }

    public static BankAccountValidationException GlAccountMustBeAsset(
        Guid glAccountId,
        AccountType actualType,
        Guid? bankAccountId = null)
    {
        const string message = "Bank account must point to an Asset GL account.";
        return new(
            message,
            "pm.validation.bank_account.gl_account_must_be_asset",
            BuildContext(
                bankAccountId: bankAccountId,
                glAccountId: glAccountId,
                actualType: actualType.ToString(),
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["gl_account_id"] = [message]
                }));
    }

    public static BankAccountValidationException GlAccountCannotRequireDimensions(
        Guid glAccountId,
        Guid? bankAccountId = null)
    {
        const string message = "Bank account GL account must not require dimensions.";
        return new(
            message,
            "pm.validation.bank_account.gl_account_dimensions_not_allowed",
            BuildContext(
                bankAccountId: bankAccountId,
                glAccountId: glAccountId,
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["gl_account_id"] = [message]
                }));
    }

    public static BankAccountValidationException MultipleActiveDefaults(Guid? bankAccountId = null)
    {
        const string message = "Only one active bank account can be marked as default.";
        return new(
            message,
            "pm.validation.bank_account.multiple_active_defaults",
            BuildContext(
                bankAccountId: bankAccountId,
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["is_default"] = [message]
                }));
    }

    private static IReadOnlyDictionary<string, object?> BuildContext(
        Guid? bankAccountId = null,
        Guid? glAccountId = null,
        string? actualType = null,
        string? last4 = null,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        var ctx = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (bankAccountId is not null)
            ctx["catalogId"] = bankAccountId.Value;
        if (glAccountId is not null)
            ctx["glAccountId"] = glAccountId.Value;
        if (!string.IsNullOrWhiteSpace(actualType))
            ctx["actualType"] = actualType;
        if (!string.IsNullOrWhiteSpace(last4))
            ctx["last4"] = last4;
        if (errors is not null)
            ctx["errors"] = errors;
        return ctx;
    }
}
