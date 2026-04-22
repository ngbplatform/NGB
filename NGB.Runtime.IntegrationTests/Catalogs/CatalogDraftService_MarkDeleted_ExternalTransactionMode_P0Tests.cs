using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Definitions;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Catalogs;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Catalogs;

/// <summary>
/// P0: CatalogDraftService supports external transaction mode (manageTransaction=false).
/// This file covers the MarkForDeletionAsync branch that is easy to miss:
/// - fail fast when manageTransaction=false is used without an active transaction
/// - external commit persists both the registry flag and the audit event
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CatalogDraftService_MarkDeleted_ExternalTransactionMode_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string CatalogCode = "it_cat_tx";

    [Fact]
    public async Task MarkForDeletionAsync_ManageTransactionFalse_WithoutActiveTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestCatalogContributor>());

        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();

        var catalogId = await drafts.CreateAsync(CatalogCode, manageTransaction: true, ct: CancellationToken.None);

        var act = () => drafts.MarkForDeletionAsync(catalogId, manageTransaction: false, ct: CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("This operation requires an active transaction.");
    }

    [Fact]
    public async Task MarkForDeletionAsync_ManageTransactionFalse_ExternalCommit_Persists_IsDeleted_And_Audit()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestCatalogContributor>());

        Guid catalogId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
            catalogId = await drafts.CreateAsync(CatalogCode, manageTransaction: true, ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            await drafts.MarkForDeletionAsync(catalogId, manageTransaction: false, ct: CancellationToken.None);

            // If the service accidentally manages its own transaction here, this commit would either throw or
            // persist unexpectedly early. We validate the final state after external commit.
            await uow.CommitAsync(CancellationToken.None);
        }

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var isDeleted = await conn.ExecuteScalarAsync<bool>(
            "SELECT is_deleted FROM catalogs WHERE id = @id;",
            new { id = catalogId });

        isDeleted.Should().BeTrue();

        var auditCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_audit_events WHERE entity_kind = @k AND entity_id = @id AND action_code = @a;",
            new { k = (short)AuditEntityKind.Catalog, id = catalogId, a = AuditActionCodes.CatalogMarkForDeletion });

        auditCount.Should().Be(1);
    }
}
