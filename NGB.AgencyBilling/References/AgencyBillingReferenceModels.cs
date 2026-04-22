using NGB.AgencyBilling.Enums;

namespace NGB.AgencyBilling.References;

public sealed record AgencyBillingClientReference(
    Guid Id,
    bool IsMarkedForDeletion,
    string? Display,
    bool IsActive,
    AgencyBillingClientStatus? Status,
    Guid? PaymentTermsId);

public sealed record AgencyBillingProjectReference(
    Guid Id,
    bool IsMarkedForDeletion,
    string? Display,
    bool IsActive,
    Guid? ClientId,
    AgencyBillingProjectStatus? Status);

public sealed record AgencyBillingTeamMemberReference(
    Guid Id,
    bool IsMarkedForDeletion,
    string? Display,
    bool IsActive);

public sealed record AgencyBillingServiceItemReference(
    Guid Id,
    bool IsMarkedForDeletion,
    string? Display,
    bool IsActive);

public sealed record AgencyBillingPaymentTermsReference(
    Guid Id,
    bool IsMarkedForDeletion,
    string? Display,
    bool IsActive);
