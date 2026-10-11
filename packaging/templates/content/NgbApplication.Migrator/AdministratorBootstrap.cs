using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.UnitOfWork;

namespace NgbApplication.Migrator;

internal static class AdministratorBootstrap
{
    public static Task EnsureAsync(IPlatformRoleRepository roles, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        return unitOfWork.ExecuteInUowTransactionAsync(async token =>
        {
            if (await roles.GetByCodeAsync(ApplicationRoles.Administrator, token) is null)
            {
                await roles.UpsertAsync(
                    Guid.Parse("019a0c12-8000-7000-8000-000000000001"),
                    ApplicationRoles.Administrator,
                    "Administrator",
                    "Full application access for active users.",
                    isSystem: true,
                    isActive: true,
                    token);
            }
        }, ct);
    }
}
