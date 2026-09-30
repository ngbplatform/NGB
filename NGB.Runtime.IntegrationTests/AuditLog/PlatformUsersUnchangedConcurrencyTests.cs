using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(AccountingPostgresCollection.Name)]
public sealed class PlatformUsersUnchangedConcurrencyTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData("unchanged@example.com", "Performance User", false)]
    [InlineData("unchanged@example.com", "Performance User", true)]
    [InlineData(null, null, false)]
    public async Task UnchangedActor_DoesNotBlockAnotherTransaction(
        string? email, string? displayName, bool changeSecondDisplayName)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        const string subject = "performance-unchanged-actor";
        Guid userId;
        await using (var seed = host.Services.CreateAsyncScope())
        {
            var uow = seed.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var users = seed.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
            await uow.BeginTransactionAsync();
            userId = await users.UpsertAsync(subject, email, displayName, true);
            await uow.CommitAsync();
        }

        await using var first = host.Services.CreateAsyncScope();
        var firstUow = first.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var firstUsers = first.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
        var before = (await firstUsers.GetByIdAsync(userId))!;
        await firstUow.BeginTransactionAsync();
        try
        {
            // Exercise the same input normalization as the mutating path.
            (await firstUsers.UpsertAsync($" {subject} ", $" {email} ", $" {displayName} ", true))
                .Should().Be(userId);
            (await firstUsers.GetByIdAsync(userId))!.UpdatedAtUtc.Should().Be(before.UpdatedAtUtc);

            await using var second = host.Services.CreateAsyncScope();
            var secondUow = second.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var secondUsers = second.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
            await secondUow.BeginTransactionAsync();
            // The first business transaction deliberately remains open. A redundant
            // email/row lock would prevent this operation from completing at all.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var secondName = changeSecondDisplayName ? "Updated User" : displayName;
            (await secondUsers.UpsertAsync(subject, email, secondName, true, deadline.Token))
                .Should().Be(userId);
            await secondUow.CommitAsync();
        }
        finally
        {
            await firstUow.RollbackAsync();
        }

        var after = (await firstUsers.GetByIdAsync(userId))!;
        after.DisplayName.Should().Be(changeSecondDisplayName ? "Updated User" : displayName);
        after.CreatedAtUtc.Should().Be(before.CreatedAtUtc);
        if (!changeSecondDisplayName)
            after.UpdatedAtUtc.Should().Be(before.UpdatedAtUtc);
    }

    [Fact]
    public async Task UnchangedActor_ReadsOwnInsert_AndRollbackDoesNotPersistUser()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
        const string subject = "performance-rollback-actor";
        await uow.BeginTransactionAsync();
        var userId = await users.UpsertAsync(subject, "rollback@example.com", "Actor", true);
        (await users.UpsertAsync(subject, "rollback@example.com", "Actor", true)).Should().Be(userId);
        await uow.RollbackAsync();
        (await users.GetByAuthSubjectAsync(subject)).Should().BeNull();
    }
}
