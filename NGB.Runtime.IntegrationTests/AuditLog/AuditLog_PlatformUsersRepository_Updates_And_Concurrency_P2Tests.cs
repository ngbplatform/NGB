using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_PlatformUsersRepository_Updates_And_Concurrency_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task UpsertSameAuthSubject_UpdatesFields_PreservesUserId_AndAdvancesUpdatedAtUtc()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string subject = "kc|p2-user-update";

        Guid userId1;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            userId1 = await users.UpsertAsync(
                authSubject: subject,
                email: "u1@example.com",
                displayName: "User One",
                isActive: true,
                ct: CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
        }

        PlatformUser user1;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
            user1 = (await users.GetByAuthSubjectAsync(subject, CancellationToken.None))!;
        }

        // Ensure UpdatedAtUtc has a chance to advance.
        await Task.Delay(25);

        Guid userId2;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            userId2 = await users.UpsertAsync(
                authSubject: subject,
                email: "u2@example.com",
                displayName: "User Two",
                isActive: false,
                ct: CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
        }

        PlatformUser user2;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
            user2 = (await users.GetByAuthSubjectAsync(subject, CancellationToken.None))!;
        }

        userId2.Should().Be(userId1);
        user2.UserId.Should().Be(userId1);
        user2.AuthSubject.Should().Be(subject);

        user2.Email.Should().Be("u2@example.com");
        user2.DisplayName.Should().Be("User Two");
        user2.IsActive.Should().BeFalse();

        user2.CreatedAtUtc.Should().Be(user1.CreatedAtUtc);
        user2.UpdatedAtUtc.Should().BeAfter(user1.UpdatedAtUtc);
        user2.UpdatedAtUtc.Should().BeOnOrAfter(user2.CreatedAtUtc);
    }


    [Fact]
    public async Task ConcurrentUpsert_SameAuthSubject_ReturnsSameUserId_AndSingleRowExists()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string subject = "kc|p2-user-concurrent";

        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int readyCount = 0;

        async Task<Guid> UpsertInOwnTransactionAsync()
        {
            await using var scope = host.Services.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            if (Interlocked.Increment(ref readyCount) == 2)
                ready.TrySetResult();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await ready.Task.WaitAsync(cts.Token);

            var id = await users.UpsertAsync(
                authSubject: subject,
                email: "concurrent@example.com",
                displayName: "Concurrent",
                isActive: true,
                ct: CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
            return id;
        }

        var t1 = Task.Run(UpsertInOwnTransactionAsync);
        var t2 = Task.Run(UpsertInOwnTransactionAsync);

        var ids = await Task.WhenAll(t1, t2);

        ids[0].Should().Be(ids[1]);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
            new { s = subject });

        count.Should().Be(1);

        // Dapper.AOT: avoid `dynamic` materialization.
        var row = await conn.QuerySingleAsync<PlatformUserRow>(
            """
            SELECT
                user_id AS "UserId",
                auth_subject AS "AuthSubject",
                email AS "Email",
                display_name AS "DisplayName",
                is_active AS "IsActive"
            FROM platform_users
            WHERE auth_subject = @s;
            """,
            new { s = subject });

        row.UserId.Should().Be(ids[0]);
        row.AuthSubject.Should().Be(subject);
        row.Email.Should().Be("concurrent@example.com");
        row.DisplayName.Should().Be("Concurrent");
        row.IsActive.Should().BeTrue();
    }

    private sealed class PlatformUserRow
    {
        public Guid UserId { get; set; }
        public string AuthSubject { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }
}
