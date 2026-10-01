using NGB.Application.Abstractions.BusinessObjects;
using NGB.Contracts.BusinessObjects;
using NGB.Core.Security;
using NGB.Persistence.AuditLog;
using NGB.Runtime.Security;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.BusinessObjects;

internal sealed class BusinessObjectContentAccess(
    IBusinessObjectResolver resolver,
    INgbAccessChecker access,
    ICurrentActorContext actorContext,
    IPlatformUserRepository users)
{
    public async Task RequireAsync(BusinessObjectRef target, string capability, string action, CancellationToken ct)
    {
        await access.RequireAsync(NgbResourceKinds.System, capability, action, ct);
        await resolver.ResolveAsync(target, ct);
    }

    public async Task<Guid> ActorAsync(CancellationToken ct)
    {
        var actor = actorContext.Current;
        if (actor is null || !actor.IsActive)
            throw new BusinessObjectException("business_object.actor_required", "An active authenticated user is required.", NgbErrorKind.Forbidden);
        
        return await users.UpsertAsync(actor.AuthSubject, actor.Email, actor.DisplayName, actor.IsActive, ct);
    }

    public static void ValidatePage(int limit, Guid? cursor)
    {
        if (limit is < 1 or > 100 || cursor == Guid.Empty)
            throw new BusinessObjectException("business_object.invalid_page", "Limit must be between 1 and 100 and cursor must be a nonempty resource ID.");
    }
}
