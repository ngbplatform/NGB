using Dapper;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Catalogs;
using CertificationApp.Definitions;

namespace CertificationApp.PostgreSql;

public sealed class CheckpointStore(IUnitOfWork uow) : ICheckpointStore
{
    public async Task CaptureOnceAsync(Guid operationId, CancellationToken ct)
    {
        await uow.EnsureConnectionOpenAsync(ct);
        await uow.Connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO certification_checkpoint_effects (operation_id) VALUES (@operationId) ON CONFLICT DO NOTHING",
            new { operationId },
            cancellationToken: ct));
    }

    public async Task<int> GetEffectCountAsync(Guid operationId, CancellationToken ct)
    {
        await uow.EnsureConnectionOpenAsync(ct);
        return await uow.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM certification_checkpoint_effects WHERE operation_id = @operationId",
            new { operationId },
            cancellationToken: ct));
    }
}

public static class CheckpointPostgresRegistration
{
    public static IServiceCollection AddCheckpointPostgres(this IServiceCollection services)
    {
        services.AddScoped<ICheckpointStore, CheckpointStore>();
        services.AddScoped<ICatalogTypeStorage>(provider => new PostgresHeadCatalogTypeStorage(
            provider.GetRequiredService<IUnitOfWork>(),
            CheckpointDefinitions.CatalogCode,
            "cat_certification_checkpoint",
            [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        return services;
    }
}
