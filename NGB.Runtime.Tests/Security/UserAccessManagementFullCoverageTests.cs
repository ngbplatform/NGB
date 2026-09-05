using FluentAssertions;
using Moq;
using NGB.Contracts.Common;
using NGB.Contracts.Security;
using NGB.Core.AuditLog;
using NGB.Core.Security;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Security;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Security;

public sealed class UserAccessManagementFullCoverageTests
{
    [Fact]
    public async Task PublicOperations_RejectEveryNullMissingAndInvalidEmailBoundary()
    {
        var fixture = new Fixture();
        var id = Guid.NewGuid();

        await ((Func<Task>)(() => fixture.Sut.GetUsersAsync(null!, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => fixture.Sut.GetUserAsync(id, default)))
            .Should().ThrowAsync<SecurityUserNotFoundException>();
        await ((Func<Task>)(() => fixture.Sut.CreateUserAsync(null!, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => fixture.Sut.CreateUserAsync(Create(" "), default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => fixture.Sut.CreateUserAsync(Create("User <user@example.com>"), default)))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        await ((Func<Task>)(() => fixture.Sut.UpdateUserAsync(id, null!, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => fixture.Sut.UpdateUserAsync(id, Update(), default)))
            .Should().ThrowAsync<SecurityUserNotFoundException>();
        await ((Func<Task>)(() => fixture.Sut.ReplaceUserRolesAsync(id, null!, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => fixture.Sut.ReplaceUserRolesAsync(id, new(null!), default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => fixture.Sut.ReplaceUserRolesAsync(id, new([]), default)))
            .Should().ThrowAsync<SecurityUserNotFoundException>();
        await ((Func<Task>)(() => fixture.Sut.ReactivateUserAsync(id, default)))
            .Should().ThrowAsync<SecurityUserNotFoundException>();
    }

    [Fact]
    public async Task UserMutations_RejectOversizedRoleCollectionsBeforeExternalCalls()
    {
        var fixture = new Fixture();
        var roles = Enumerable.Repeat(Guid.NewGuid(), UserAccessManagementService.MaxRoleAssignmentsPerUser + 1).ToArray();

        await ((Func<Task>)(() => fixture.Sut.CreateUserAsync(
                new("user@example.com", null, null, null, true, null, false, roles), default)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => fixture.Sut.UpdateUserAsync(
                Guid.NewGuid(), new(null, null, null, null, true, null, false, roles), default)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => fixture.Sut.ReplaceUserRolesAsync(
                Guid.NewGuid(), new(roles), default)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task CreateUser_CoversFailureAuditAndCancellationFilter()
    {
        var failed = new Fixture();
        failed.IdentityProvider.Setup(x => x.FindUserByEmailAsync("user@example.com", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider failed"));

        await ((Func<Task>)(() => failed.Sut.CreateUserAsync(Create("user@example.com"), default)))
            .Should().ThrowAsync<InvalidOperationException>();
        failed.OperationStatuses.Should().Equal("Pending", "Failed");

        var cancelled = new Fixture();
        cancelled.IdentityProvider.Setup(x => x.FindUserByEmailAsync("user@example.com", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        await ((Func<Task>)(() => cancelled.Sut.CreateUserAsync(Create("user@example.com"), default)))
            .Should().ThrowAsync<OperationCanceledException>();
        cancelled.OperationStatuses.Should().Equal("Pending");
    }

    [Fact]
    public async Task GetUsers_CoversEmptyFallbackBatchRoleAndUserOrderingAndAllIdentityFallbacks()
    {
        var fixture = new Fixture();
        var now = DateTime.UtcNow;
        var a = User(Guid.NewGuid(), "id-a", "a@example.com", "Zulu");
        var b = User(Guid.NewGuid(), "missing-b", "b@example.com", null);
        var c = User(Guid.NewGuid(), " ", " ", null);
        var d = User(Guid.NewGuid(), "missing-d", null, null);
        fixture.Users.Setup(x => x.GetPageAsync(0, PagingLimits.DefaultPageSize, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserPage([a, b, c, d], 4));
        fixture.UserRoles.Setup(x => x.GetRolesForUsersAsync(
                It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<PlatformRole>>
            {
                [a.UserId] =
                [
                    new PlatformRole(Guid.NewGuid(), "z", "Zulu", null, false, true, now, now),
                    new PlatformRole(Guid.NewGuid(), "a", "Alpha", null, true, false, now, now)
                ]
            });
        fixture.IdentityProvider.Setup(x => x.GetUsersByIdsAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IdentityProviderUserDto>
            {
                ["id-a"] = Idp("id-a", "a@example.com", enabled: true)
            });
        fixture.IdentityProvider.Setup(x => x.FindUsersByEmailsAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IdentityProviderUserDto>(StringComparer.OrdinalIgnoreCase)
            {
                ["b@example.com"] = Idp("id-b", "b@example.com", enabled: false)
            });

        var result = await fixture.Sut.GetUsersAsync(new UserPageRequestDto(), default);

        result.Items.Should().HaveCount(4);
        result.Total.Should().Be(4);
        result.Items.Single(x => x.UserId == a.UserId).Roles.Select(x => x.Name).Should().Equal("Alpha", "Zulu");
        result.Items.Single(x => x.UserId == a.UserId).KeycloakEnabled.Should().BeTrue();
        result.Items.Single(x => x.UserId == b.UserId).KeycloakEnabled.Should().BeFalse();
        result.Items.Single(x => x.UserId == c.UserId).KeycloakEnabled.Should().BeFalse();
        result.Items.Single(x => x.UserId == d.UserId).KeycloakEnabled.Should().BeFalse();

        var allPresent = new Fixture();
        allPresent.Users.Setup(x => x.GetPageAsync(0, PagingLimits.DefaultPageSize, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserPage([a], 1));
        allPresent.UserRoles.Setup(x => x.GetRolesForUsersAsync(
                It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<PlatformRole>>());
        allPresent.IdentityProvider.Setup(x => x.GetUsersByIdsAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IdentityProviderUserDto> { ["id-a"] = Idp("id-a", "a@example.com") });
        (await allPresent.Sut.GetUsersAsync(new UserPageRequestDto(), default)).Items.Should().ContainSingle();
        allPresent.IdentityProvider.Verify(x => x.FindUsersByEmailsAsync(
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetUsers_CursorCarriesTotalAndIsBoundToActiveFilter()
    {
        var fixture = new Fixture();
        var first = User(Guid.NewGuid(), "first", null, "First");
        var second = User(Guid.NewGuid(), "second", null, "Second");
        fixture.Users.Setup(x => x.GetPageAsync(0, 1, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserPage([first], 2));
        fixture.Users.Setup(x => x.GetCursorPageAsync(
                It.Is<PlatformUserPageCursor>(cursor => cursor.Offset == 1 && cursor.Total == 2),
                1,
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserPage([second], 2));
        fixture.UserRoles.Setup(x => x.GetRolesForUsersAsync(
                It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<PlatformRole>>());
        fixture.IdentityProvider.Setup(x => x.GetUsersByIdsAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IdentityProviderUserDto>());

        var firstPage = await fixture.Sut.GetUsersAsync(new UserPageRequestDto(0, 1, true), default);
        var secondPage = await fixture.Sut.GetUsersAsync(
            new UserPageRequestDto(0, 1, true, firstPage.NextCursor),
            default);

        firstPage.NextCursor.Should().NotBeNullOrWhiteSpace();
        secondPage.Items.Should().ContainSingle().Which.UserId.Should().Be(second.UserId);
        secondPage.NextCursor.Should().BeNull();
        Func<Task> changedFilter = () => fixture.Sut.GetUsersAsync(
            new UserPageRequestDto(0, 1, false, firstPage.NextCursor),
            default);
        await changedFilter.Should().ThrowAsync<NgbArgumentInvalidException>();
    }

    [Fact]
    public async Task GetUser_CoversNullIdentityFieldsVersionFallbackAndRoleMapping()
    {
        var fixture = new Fixture();
        var first = User(Guid.NewGuid(), "first", null, null);
        var second = User(Guid.NewGuid(), "second", "stored@example.com", "Stored");
        fixture.Users.SetupSequence(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(first)
            .ReturnsAsync(second);
        fixture.IdentityProvider.SetupSequence(x => x.GetUserByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityProviderUserDto?)null)
            .ReturnsAsync(new IdentityProviderUserDto("second", null, null, null, null, false));
        fixture.UserRoles.Setup(x => x.GetRolesForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlatformRole(Guid.NewGuid(), "role", "Role", null, true, false, DateTime.UtcNow, DateTime.UtcNow)]);
        fixture.Versions.SetupSequence(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUserAccessVersion?)null)
            .ReturnsAsync(new PlatformUserAccessVersion(second.UserId, 4, DateTime.UtcNow));

        var withoutIdp = await fixture.Sut.GetUserAsync(first.UserId, default);
        var nullIdpFields = await fixture.Sut.GetUserAsync(second.UserId, default);

        withoutIdp.Email.Should().BeNull();
        withoutIdp.DisplayName.Should().BeNull();
        withoutIdp.KeycloakEnabled.Should().BeNull();
        withoutIdp.AccessVersion.Should().Be(1);
        nullIdpFields.Email.Should().Be("stored@example.com");
        nullIdpFields.DisplayName.Should().Be("Stored");
        nullIdpFields.Roles.Should().ContainSingle();
    }

    [Fact]
    public async Task CreateUser_CoversNullIdpFieldsRoleAuditAndExistingUserReadbackFallback()
    {
        var fixture = new Fixture();
        var userId = Guid.NewGuid();
        var role = new PlatformRole(Guid.NewGuid(), "role", "Role", null, false, true, DateTime.UtcNow, DateTime.UtcNow);
        fixture.IdentityProvider.Setup(x => x.FindUserByEmailAsync("user@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityProviderUserDto?)null);
        fixture.IdentityProvider.Setup(x => x.CreateUserAsync(
                It.IsAny<CreateIdentityProviderUserRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityProviderUserDto("new-id", null, null, null, null, true));
        fixture.IdentityProvider.Setup(x => x.GetUserByIdAsync("new-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityProviderUserDto?)null);
        fixture.Users.Setup(x => x.UpsertAsync(
                "new-id", "user@example.com", "user@example.com", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userId);
        fixture.Users.Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(User(userId, "new-id", "user@example.com", null));
        fixture.UserRoles.Setup(x => x.GetRolesForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([role]);
        fixture.Versions.Setup(x => x.GetOrCreateAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserAccessVersion(userId, 1, DateTime.UtcNow));
        fixture.Versions.Setup(x => x.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUserAccessVersion?)null);

        var result = await fixture.Sut.CreateUserAsync(
            Create("user@example.com") with { DisplayName = null, RoleIds = [role.RoleId] }, default);

        result.UserId.Should().Be(userId);
        fixture.AuditKinds.Should().Contain(AuditEntityKind.SecurityRole);

        var existing = new Fixture();
        var existingIdp = Idp("existing", "old@example.com");
        existing.IdentityProvider.Setup(x => x.FindUserByEmailAsync("user@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingIdp);
        existing.IdentityProvider.Setup(x => x.GetUserByIdAsync("existing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityProviderUserDto?)null);
        existing.Users.Setup(x => x.UpsertAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(userId);
        existing.Users.Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(User(userId, "existing", "user@example.com", "User"));
        existing.UserRoles.Setup(x => x.GetRolesForUserAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        existing.Versions.Setup(x => x.GetOrCreateAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserAccessVersion(userId, 1, DateTime.UtcNow));
        existing.Versions.Setup(x => x.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUserAccessVersion?)null);
        (await existing.Sut.CreateUserAsync(Create("user@example.com"), default)).UserId.Should().Be(userId);
    }

    [Fact]
    public async Task UpdateUser_CoversOptionalEmailCurrentIdpMissingAndNullNameFallbacks()
    {
        var fixture = new Fixture();
        var userId = Guid.NewGuid();
        var user = User(userId, "id", "user@example.com", null);
        fixture.Users.Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        fixture.Users.Setup(x => x.UpsertAsync(
                "id", "user@example.com", "user@example.com", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userId);
        fixture.IdentityProvider.SetupSequence(x => x.GetUserByIdAsync("id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Idp("id", "user@example.com"))
            .ReturnsAsync((IdentityProviderUserDto?)null)
            .ReturnsAsync((IdentityProviderUserDto?)null);
        fixture.UserRoles.Setup(x => x.GetRolesForUserAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        fixture.Versions.Setup(x => x.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUserAccessVersion?)null);

        var result = await fixture.Sut.UpdateUserAsync(userId, Update() with { Email = null }, default);

        result.Email.Should().Be("user@example.com");
    }

    [Fact]
    public async Task Reactivate_CoversDirectSubjectProvisioningMismatchAndMissingEmailConfiguration()
    {
        var direct = new Fixture();
        var id = Guid.NewGuid();
        var directUser = User(id, "direct", "direct@example.com", "Direct", active: false);
        direct.Users.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(directUser);
        direct.IdentityProvider.Setup(x => x.GetUserByIdAsync("direct", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Idp("direct", "direct@example.com", enabled: false));
        await direct.Sut.ReactivateUserAsync(id, default);
        direct.Users.Verify(x => x.SetActiveAsync(id, true, It.IsAny<CancellationToken>()), Times.Once);

        var caseInsensitive = new Fixture();
        var caseInsensitiveUser = User(id, "case-id", "user@example.com", "User", active: false);
        caseInsensitive.Users.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(caseInsensitiveUser);
        caseInsensitive.IdentityProvider.Setup(x => x.GetUserByIdAsync("case-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityProviderUserDto?)null);
        caseInsensitive.IdentityProvider.Setup(x => x.FindUsersByEmailsAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IdentityProviderUserDto>(StringComparer.Ordinal)
            {
                ["USER@EXAMPLE.COM"] = Idp("case-id", "user@example.com", enabled: false)
            });
        await caseInsensitive.Sut.ReactivateUserAsync(id, default);
        caseInsensitive.Users.Verify(
            x => x.SetActiveAsync(id, true, It.IsAny<CancellationToken>()), Times.Once);

        var mismatch = new Fixture();
        var stale = User(id, "stale", "user@example.com", "User");
        mismatch.Users.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(stale);
        mismatch.IdentityProvider.Setup(x => x.GetUserByIdAsync("stale", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityProviderUserDto?)null);
        mismatch.IdentityProvider.Setup(x => x.FindUsersByEmailsAsync(
                It.Is<IReadOnlyList<string>>(emails => emails.SequenceEqual(new[] { "user@example.com" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IdentityProviderUserDto>(StringComparer.OrdinalIgnoreCase));
        mismatch.IdentityProvider.Setup(x => x.CreateUserAsync(
                It.Is<CreateIdentityProviderUserRequest>(request => request.Email == "user@example.com" && request.Enabled),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Idp("new-id", "user@example.com"));
        mismatch.Users.Setup(x => x.UpsertAsync(
                "new-id", "user@example.com", "User", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        await ((Func<Task>)(() => mismatch.Sut.ReactivateUserAsync(id, default)))
            .Should().ThrowAsync<NgbInvariantViolationException>();

        var missingEmail = new Fixture();
        missingEmail.Users.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(User(id, "stale", null, null));
        missingEmail.IdentityProvider.Setup(x => x.GetUserByIdAsync("stale", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityProviderUserDto?)null);
        await ((Func<Task>)(() => missingEmail.Sut.ReactivateUserAsync(id, default)))
            .Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    private static CreateUserRequestDto Create(string email)
        => new(email, null, null, "User", true, null, false, []);

    private static UpdateUserRequestDto Update()
        => new("user@example.com", null, null, null, true, null, false, []);

    private static PlatformUser User(
        Guid id,
        string subject,
        string? email,
        string? display,
        bool active = true)
        => new(id, subject, email, display, active, DateTime.UtcNow, DateTime.UtcNow);

    private static IdentityProviderUserDto Idp(
        string id,
        string? email,
        bool enabled = true)
        => new(id, email, null, null, null, enabled);

    private sealed class Fixture
    {
        public Fixture()
        {
            Uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
            Uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            UserRoles.Setup(x => x.ReplaceUserRolesAsync(
                    It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Versions.Setup(x => x.GetOrCreateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => new PlatformUserAccessVersion(id, 1, DateTime.UtcNow));
            Versions.Setup(x => x.IncrementAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => new PlatformUserAccessVersion(id, 2, DateTime.UtcNow));
            Operations.Setup(x => x.UpsertAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .Callback<Guid, string, string?, string?, Guid?, string, string?, Guid?, CancellationToken>(
                    (_, _, _, _, _, status, _, _, _) => OperationStatuses.Add(status))
                .Returns<Guid, string, string?, string?, Guid?, string, string?, Guid?, CancellationToken>(
                    (operationId, type, email, keycloakId, platformId, status, error, requestedBy, _) =>
                        Task.FromResult(new UserProvisioningOperation(
                            operationId, type, email, keycloakId, platformId, status, error, requestedBy,
                            DateTime.UtcNow, DateTime.UtcNow)));
            IdentityProvider.Setup(x => x.UpdateUserAsync(
                    It.IsAny<string>(), It.IsAny<UpdateIdentityProviderUserRequest>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            IdentityProvider.Setup(x => x.SetUserEnabledAsync(
                    It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Users.Setup(x => x.SetActiveAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Audit.Setup(x => x.WriteAsync(
                    It.IsAny<AuditEntityKind>(), It.IsAny<Guid>(), It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<AuditFieldChange>?>(), It.IsAny<object?>(), It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .Callback<AuditEntityKind, Guid, string, IReadOnlyList<AuditFieldChange>?, object?, Guid?, CancellationToken>(
                    (kind, _, _, _, _, _, _) => AuditKinds.Add(kind))
                .Returns(Task.CompletedTask);
            Audit.Setup(x => x.WriteBatchAsync(
                    It.IsAny<IReadOnlyList<AuditLogWriteRequest>>(), It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyList<AuditLogWriteRequest>, CancellationToken>((requests, _) =>
                    AuditKinds.AddRange(requests.Select(static request => request.EntityKind)))
                .Returns(Task.CompletedTask);

            Sut = new UserAccessManagementService(
                Uow.Object,
                Users.Object,
                UserRoles.Object,
                Versions.Object,
                Operations.Object,
                IdentityProvider.Object,
                Audit.Object);
        }

        public Mock<IUnitOfWork> Uow { get; } = new(MockBehavior.Loose);
        public Mock<IPlatformUserRepository> Users { get; } = new(MockBehavior.Loose);
        public Mock<IPlatformUserRoleRepository> UserRoles { get; } = new(MockBehavior.Loose);
        public Mock<IUserAccessVersionRepository> Versions { get; } = new(MockBehavior.Loose);
        public Mock<IUserProvisioningOperationRepository> Operations { get; } = new(MockBehavior.Loose);
        public Mock<IIdentityProviderUserAdminClient> IdentityProvider { get; } = new(MockBehavior.Loose);
        public Mock<IAuditLogService> Audit { get; } = new(MockBehavior.Loose);
        public List<string> OperationStatuses { get; } = [];
        public List<AuditEntityKind> AuditKinds { get; } = [];
        public UserAccessManagementService Sut { get; }
    }
}
