using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Core.Catalogs.Exceptions;
using NGB.Definitions;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Catalogs.Storage;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Catalogs;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_CatalogLifecycle_P2Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    private const string CatalogCodeCreate = "it_cat_audit_create";
    private const string CatalogCodeDelete = "it_cat_audit_delete";
    private const string CatalogCodeFailCreate = "it_cat_audit_fail_create";
    private const string CatalogCodeFailDelete = "it_cat_audit_fail_delete";

    [Fact]
    public async Task CreateCatalog_WritesAuditEvent_WithMetadata_ChangesAndUpsertsActor()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestCatalogContributor>();
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|cat-p2-user-1",
                        Email: "cat.p2.user1@example.com",
                        DisplayName: "Catalog P2 User 1")));
            });

        Guid catalogId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
            catalogId = await drafts.CreateAsync(CatalogCodeCreate, manageTransaction: true, ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Catalog,
                    EntityId: catalogId,
                    ActionCode: AuditActionCodes.CatalogCreate,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().ContainSingle();
            var ev = events.Single();

            ev.ActorUserId.Should().NotBeNull();

            var user = await users.GetByAuthSubjectAsync("kc|cat-p2-user-1", CancellationToken.None);
            user.Should().NotBeNull();
            user!.UserId.Should().Be(ev.ActorUserId!.Value);

            JsonString(ev.MetadataJson).Should().BeNull("MetadataJson is an object, not a string");
            using var meta = JsonDocument.Parse(ev.MetadataJson!);
            meta.RootElement.GetProperty("catalogCode").GetString().Should().Be(CatalogCodeCreate);

            ev.Changes.Select(c => c.FieldPath).Should().Equal(["catalog_code", "is_deleted"]);

            var c0 = ev.Changes[0];
            c0.FieldPath.Should().Be("catalog_code");
            c0.OldValueJson.Should().BeNull();
            JsonString(c0.NewValueJson).Should().Be(CatalogCodeCreate);

            var c1 = ev.Changes[1];
            c1.FieldPath.Should().Be("is_deleted");
            c1.OldValueJson.Should().BeNull();
            JsonBool(c1.NewValueJson).Should().BeFalse();
        }
    }

    [Fact]
    public async Task MarkDeleted_WritesAuditEvent_WithMetadata_AndOldNewChangeValues()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestCatalogContributor>();
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|cat-p2-user-2",
                        Email: null,
                        DisplayName: null)));
            });

        Guid catalogId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
            catalogId = await drafts.CreateAsync(CatalogCodeDelete, manageTransaction: true, ct: CancellationToken.None);
            await drafts.MarkForDeletionAsync(catalogId, manageTransaction: true, ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();

            // Ensure actor upsert happened at least once (on create).
            (await users.GetByAuthSubjectAsync("kc|cat-p2-user-2", CancellationToken.None)).Should().NotBeNull();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Catalog,
                    EntityId: catalogId,
                    ActionCode: AuditActionCodes.CatalogMarkForDeletion,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().ContainSingle();
            var ev = events.Single();

            using var meta = JsonDocument.Parse(ev.MetadataJson!);
            meta.RootElement.GetProperty("catalogCode").GetString().Should().Be(CatalogCodeDelete);

            ev.Changes.Should().ContainSingle();
            var c = ev.Changes.Single();
            c.FieldPath.Should().Be("is_deleted");
            JsonBool(c.OldValueJson).Should().BeFalse();
            JsonBool(c.NewValueJson).Should().BeTrue();
        }
    }

    [Fact]
    public async Task MarkDeleted_Twice_IsNoOp_AndDoesNotWriteSecondAuditEvent()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestCatalogContributor>();
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|cat-p2-user-3",
                        Email: null,
                        DisplayName: null)));
            });

        Guid catalogId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
            catalogId = await drafts.CreateAsync("it_cat_audit_noop", manageTransaction: true, ct: CancellationToken.None);

            await drafts.MarkForDeletionAsync(catalogId, manageTransaction: true, ct: CancellationToken.None);
            await drafts.MarkForDeletionAsync(catalogId, manageTransaction: true, ct: CancellationToken.None); // idempotent no-op
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Catalog,
                    EntityId: catalogId,
                    ActionCode: AuditActionCodes.CatalogMarkForDeletion,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task CreateCatalog_WhenTypedStorageThrows_RollsBack_AndDoesNotWriteAuditEvent_AndDoesNotUpsertActor()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestCatalogContributor>();
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|cat-p2-user-4",
                        Email: "cat.p2.user4@example.com",
                        DisplayName: "Catalog P2 User 4")));

                services.AddScoped<ICatalogTypeStorage, ThrowOnEnsureCreatedCatalogTypeStorage>();
            });

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();

            var act = () => drafts.CreateAsync(CatalogCodeFailCreate, manageTransaction: true, ct: CancellationToken.None);
            var ex = await act.Should().ThrowAsync<CatalogTypedStorageOperationException>();
            ex.Which.AssertNgbError(CatalogTypedStorageOperationException.Code, "catalogId", "catalogCode", "operation", "details");
            ex.Which.InnerException.Should().BeOfType<NotSupportedException>();
            ex.Which.InnerException!.Message.Should().Contain("simulated catalog typed storage create failure");
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            // Registry row must not exist (transaction rollback).
            var catCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM catalogs WHERE catalog_code = @c;",
                new { c = CatalogCodeFailCreate });
            catCount.Should().Be(0);

            // Audit must not be written.
            var auditCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM platform_audit_events WHERE entity_kind = 2 AND action_code = @a;",
                new { a = AuditActionCodes.CatalogCreate });
            auditCount.Should().Be(0);

            // Actor upsert must not occur (audit was not called).
            var usersCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_users;");
            usersCount.Should().Be(0);
        }
    }

    [Fact]
    public async Task MarkDeleted_WhenTypedStorageThrows_IsNotInvoked_AndWritesAuditEvent()
    {
        // Default actor is NullCurrentActorContext (no platform_users writes).
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestCatalogContributor>();
                services.AddScoped<ICatalogTypeStorage, ThrowOnDeleteCatalogTypeStorage>();
            });

        Guid catalogId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
            catalogId = await drafts.CreateAsync(CatalogCodeFailDelete, manageTransaction: true, ct: CancellationToken.None);

            // Mark-for-deletion must NOT touch typed storage anymore, so a storage that throws on DeleteAsync must not affect it.
            await drafts.MarkForDeletionAsync(catalogId, manageTransaction: true, ct: CancellationToken.None);
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var isDeleted = await conn.ExecuteScalarAsync<bool>(
                "SELECT is_deleted FROM catalogs WHERE id = @id;",
                new { id = catalogId });
            isDeleted.Should().BeTrue();

            var auditCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM platform_audit_events WHERE entity_kind = 2 AND entity_id = @id AND action_code = @a;",
                new { id = catalogId, a = AuditActionCodes.CatalogMarkForDeletion });
            auditCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task MarkDeleted_WhenCatalogNotFound_Throws_AndDoesNotWriteAuditEvent()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestCatalogContributor>());

        var missingId = Guid.CreateVersion7();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();

            var act = () => drafts.MarkForDeletionAsync(missingId, manageTransaction: true, ct: CancellationToken.None);
            var ex = await act.Should().ThrowAsync<CatalogNotFoundException>();
            ex.Which.AssertNgbError(CatalogNotFoundException.Code, "catalogId");
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var auditCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM platform_audit_events WHERE entity_kind = 2 AND entity_id = @id AND action_code = @a;",
                new { id = missingId, a = AuditActionCodes.CatalogMarkForDeletion });

            auditCount.Should().Be(0);
        }
    }

    private static string? JsonString(string? json)
    {
        if (json is null)
            return null;

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.String
            ? doc.RootElement.GetString()
            : null;
    }

    private static bool? JsonBool(string? json)
    {
        if (json is null)
            return null;

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.True
            ? true
            : doc.RootElement.ValueKind == JsonValueKind.False
                ? false
                : null;
    }

    sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }

    sealed class ThrowOnEnsureCreatedCatalogTypeStorage : ICatalogTypeStorage
    {
        public string CatalogCode => CatalogCodeFailCreate;

        public Task EnsureCreatedAsync(Guid catalogId, CancellationToken ct = default)
            => throw new NotSupportedException("simulated catalog typed storage create failure");

        public Task DeleteAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
    }

    sealed class ThrowOnDeleteCatalogTypeStorage : ICatalogTypeStorage
    {
        public string CatalogCode => CatalogCodeFailDelete;

        public Task EnsureCreatedAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeleteAsync(Guid catalogId, CancellationToken ct = default)
            => throw new NotSupportedException("simulated catalog typed storage delete failure");
    }
}
