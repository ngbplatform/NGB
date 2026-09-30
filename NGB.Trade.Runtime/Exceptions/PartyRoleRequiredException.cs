using NGB.Tools.Exceptions;

namespace NGB.Trade.Runtime.Exceptions;

public sealed class PartyRoleRequiredException(Guid catalogId) : NgbValidationException(
    "Select at least one role: Customer or Vendor.",
    "trd.validation.party.role_required",
    new Dictionary<string, object?>
    {
        ["catalogId"] = catalogId,
        ["errors"] = new Dictionary<string, string[]>
        {
            ["is_customer"] = ["Select at least one role: Customer or Vendor."],
            ["is_vendor"] = ["Select at least one role: Customer or Vendor."]
        }
    });
