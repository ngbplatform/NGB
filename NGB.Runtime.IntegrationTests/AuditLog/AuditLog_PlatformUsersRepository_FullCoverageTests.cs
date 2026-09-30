using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(AccountingPostgresCollection.Name)]
public sealed class AuditLog_PlatformUsersRepository_FullCoverageTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ReadApis_FilterPageSeekAndBatchUsersWithoutLosingCursorState()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var seeded = new List<Guid>();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
            await uow.BeginTransactionAsync(CancellationToken.None);
            seeded.Add(await users.UpsertAsync("subject-zed", " ZED@example.com ", null, true));
            seeded.Add(await users.UpsertAsync("subject-alpha", "alpha@example.com", " Alpha ", true));
            seeded.Add(await users.UpsertAsync("subject-beta", "beta@example.com", "Beta", false));
            await uow.CommitAsync(CancellationToken.None);
        }

        await using var readScope = host.Services.CreateAsyncScope();
        var repository = readScope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();

        (await repository.GetByIdAsync(Guid.NewGuid())).Should().BeNull();
        (await repository.GetByIdAsync(seeded[1])).Should().Match<PlatformUser>(
            user => user.DisplayName == "Alpha" && user.Email == "alpha@example.com");

        var offsetPage = await repository.GetPageAsync(0, 2, null);
        offsetPage.Total.Should().Be(3);
        offsetPage.HasMore.Should().BeFalse();
        offsetPage.Items.Select(static user => user.DisplayName).Should().Equal("Alpha", "Beta");

        var activePage = await repository.GetPageAsync(int.MaxValue, int.MaxValue, true);
        activePage.Total.Should().Be(2);
        activePage.Items.Should().BeEmpty();

        var firstCursorPage = await repository.GetCursorPageAsync(
            new PlatformUserPageCursor(0, 3),
            limit: 1,
            isActive: null);
        firstCursorPage.HasMore.Should().BeTrue();
        firstCursorPage.Items.Should().ContainSingle().Which.DisplayName.Should().Be("Alpha");
        firstCursorPage.NextAfterSortKey.Should().Be("alpha");
        firstCursorPage.NextAfterUserId.Should().Be(seeded[1]);

        var secondCursorPage = await repository.GetCursorPageAsync(
            new PlatformUserPageCursor(
                Offset: 1,
                Total: firstCursorPage.Total,
                AfterSortKey: firstCursorPage.NextAfterSortKey,
                AfterUserId: firstCursorPage.NextAfterUserId),
            limit: 2,
            isActive: null);
        secondCursorPage.HasMore.Should().BeFalse();
        secondCursorPage.Total.Should().Be(3);
        secondCursorPage.Items.Select(static user => user.DisplayName).Should().Equal("Beta", null);

        (await repository.GetByIdsAsync([Guid.Empty, seeded[2], seeded[2], seeded[0]]))
            .Keys.Should().BeEquivalentTo([seeded[0], seeded[2]]);
        (await repository.GetByIdsAsync([Guid.Empty, Guid.Empty])).Should().BeEmpty();

        (await repository.GetByEmailsAsync([
                " ",
                "ALPHA@example.com",
                " alpha@example.com ",
                "missing@example.com"
            ]))
            .Should().ContainSingle().Which.UserId.Should().Be(seeded[1]);
        (await repository.GetByEmailsAsync([" ", "\t"])).Should().BeEmpty();
    }

    [Fact]
    public async Task GuardsAndSetActive_RejectInvalidArgumentsAndPersistState()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();

        await FluentActions.Invoking(() => repository.GetByIdAsync(Guid.Empty))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await FluentActions.Invoking(() => repository.GetPageAsync(-1, 1, null))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await FluentActions.Invoking(() => repository.GetPageAsync(0, 0, null))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await FluentActions.Invoking(() => repository.GetByEmailsAsync(null!))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await FluentActions.Invoking(() => repository.SetActiveAsync(Guid.Empty, false))
            .Should().ThrowAsync<NgbArgumentRequiredException>();

        await uow.BeginTransactionAsync(CancellationToken.None);
        var userId = await repository.UpsertAsync("subject-toggle", null, null, true);
        await repository.SetActiveAsync(userId, false);
        await repository.SetActiveAsync(Guid.NewGuid(), true);
        await uow.CommitAsync(CancellationToken.None);

        (await repository.GetByIdAsync(userId)).Should().Match<PlatformUser>(
            user => !user.IsActive && user.Email == null && user.DisplayName == null);
        (await repository.GetPageAsync(0, 10, false)).Items.Should().ContainSingle();
    }
}
