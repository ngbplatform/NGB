using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_PlatformUsersRepository_P0Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task UpsertUser_ThenGetByAuthSubject_Roundtrips()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid userId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            userId = await users.UpsertAsync(
                authSubject: "kc|p0-user-1",
                email: "p0.user1@example.com",
                displayName: "P0 User 1",
                isActive: true,
                ct: CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();

            var user = await users.GetByAuthSubjectAsync("kc|p0-user-1", CancellationToken.None);
            user.Should().NotBeNull();

            user!.UserId.Should().Be(userId);
            user.AuthSubject.Should().Be("kc|p0-user-1");
        }
    }

    [Fact]
    public async Task UpsertSameAuthSubjectTwice_ReturnsSameUserId()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid id1;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            id1 = await users.UpsertAsync(
                authSubject: "kc|p0-user-2",
                email: "p0.user2@example.com",
                displayName: "P0 User 2",
                isActive: true,
                ct: CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        Guid id2;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            id2 = await users.UpsertAsync(
                authSubject: "kc|p0-user-2",
                email: "p0.user2+new@example.com",
                displayName: "P0 User 2 (New)",
                isActive: true,
                ct: CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        id2.Should().Be(id1);
    }
}
