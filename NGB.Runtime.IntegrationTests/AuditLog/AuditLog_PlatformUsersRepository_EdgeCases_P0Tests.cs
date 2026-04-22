using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_PlatformUsersRepository_EdgeCases_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task UpsertAsync_WithoutActiveTransaction_Throws_AndDoesNotWrite()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();

            var act = () => users.UpsertAsync(
                authSubject: "kc|edge-user-no-tx",
                email: null,
                displayName: null,
                isActive: true,
                ct: CancellationToken.None);

            await act.Should().ThrowAsync<NgbInvariantViolationException>()
                .WithMessage("This operation requires an active transaction.");
        }

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_users;");
        count.Should().Be(0);
    }


    [Fact]
    public async Task UpsertAsync_TrimsAuthSubject_And_NormalizesWhitespaceFields_ToNull()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid userId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            userId = await users.UpsertAsync(
                authSubject: "  kc|edge-user-1  ",
                email: "   ",
                displayName: "	",
                isActive: true,
                ct: CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();

            var user = await users.GetByAuthSubjectAsync(" kc|edge-user-1 ", CancellationToken.None);
            user.Should().NotBeNull();

            user!.UserId.Should().Be(userId);
            user.AuthSubject.Should().Be("kc|edge-user-1");
            user.Email.Should().BeNull();
            user.DisplayName.Should().BeNull();
            user.IsActive.Should().BeTrue();
        }
    }


    [Fact]
    public async Task GetByAuthSubjectAsync_TrimsInput_ReturnsNullForMissing_AndRejectsWhitespace()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();

            var act = () => users.GetByAuthSubjectAsync("   ", CancellationToken.None);

            var ex = await act.Should().ThrowAsync<NgbArgumentRequiredException>();
            ex.Which.ParamName.Should().Be("authSubject");
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            await users.UpsertAsync(
                authSubject: "kc|edge-user-2",
                email: "edge.user2@example.com",
                displayName: "Edge User 2",
                isActive: true,
                ct: CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();

            var found = await users.GetByAuthSubjectAsync("  kc|edge-user-2  ", CancellationToken.None);
            found.Should().NotBeNull();

            var missing = await users.GetByAuthSubjectAsync("kc|missing-user", CancellationToken.None);
            missing.Should().BeNull();
        }
    }
}
