using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Definitions;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Catalogs;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Catalogs;

[Collection(PostgresCollection.Name)]
public sealed class CatalogDraftService_TransactionMode_AndValidation_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string CatalogCode = "it_cat_tx";

    [Fact]
    public async Task CreateAsync_WhenCatalogCodeIsBlank_Throws()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestCatalogContributor>());
        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();

        var act = () => drafts.CreateAsync("   ");
        await act.Should().ThrowAsync<NgbArgumentRequiredException>()
            .WithMessage("Catalog code is required.");
    }

    [Fact]
    public async Task CreateAsync_ManageTransactionFalse_WithoutActiveTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestCatalogContributor>());
        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();

        var act = () => drafts.CreateAsync(CatalogCode, manageTransaction: false);
        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("This operation requires an active transaction.");
    }

    [Fact]
    public async Task CreateAsync_ManageTransactionFalse_ExternalCommit_Persists_AndExternalRollback_DoesNot()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestCatalogContributor>());

        // Commit case
        Guid committedId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.BeginTransactionAsync();
            committedId = await drafts.CreateAsync(CatalogCode, manageTransaction: false);
            await uow.CommitAsync();
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();
            var count = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM catalogs WHERE id = @id;",
                new { id = committedId });
            count.Should().Be(1);
        }

        // Rollback case
        Guid rolledBackId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.BeginTransactionAsync();
            rolledBackId = await drafts.CreateAsync(CatalogCode, manageTransaction: false);
            await uow.RollbackAsync();
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();
            var count = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM catalogs WHERE id = @id;",
                new { id = rolledBackId });
            count.Should().Be(0);
        }
    }

    [Fact]
    public async Task MarkForDeletionAsync_WhenCatalogIdIsEmpty_Throws()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestCatalogContributor>());
        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();

        var act = () => drafts.MarkForDeletionAsync(Guid.Empty);
        await act.Should().ThrowAsync<NgbArgumentRequiredException>()
            .WithMessage("Catalog is required.");
    }

    [Fact]
    public async Task MarkForDeletionAsync_ManageTransactionFalse_ExternalRollback_DoesNotMarkDeleted()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestCatalogContributor>());

        Guid id;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
            id = await drafts.CreateAsync(CatalogCode);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.BeginTransactionAsync();
            await drafts.MarkForDeletionAsync(id, manageTransaction: false);
            await uow.RollbackAsync();
        }

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var isDeleted = await conn.ExecuteScalarAsync<bool>(
            "SELECT is_deleted FROM catalogs WHERE id = @id;",
            new { id });

        isDeleted.Should().BeFalse("external rollback must undo soft delete");
    }
}
