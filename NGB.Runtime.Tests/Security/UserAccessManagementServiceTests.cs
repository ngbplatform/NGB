using FluentAssertions;
using Moq;
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

public sealed class UserAccessManagementServiceTests
{
    [Fact]
    public async Task CreateUserAsync_NormalizesEmailBeforeIdentityProviderAndProjection()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var currentUser = new PlatformUser(
            userId,
            "kc-new-user",
            "new.user@example.com",
            "New User",
            IsActive: true,
            now,
            now);

        var idpUser = new IdentityProviderUserDto(
            "kc-new-user",
            "new.user@example.com",
            "New",
            "User",
            "New User",
            Enabled: true);

        var users = new Mock<IPlatformUserRepository>(MockBehavior.Strict);
        users
            .Setup(x => x.UpsertAsync(
                "kc-new-user",
                "new.user@example.com",
                "New User",
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(userId);
        users
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentUser);

        var identityProvider = new Mock<IIdentityProviderUserAdminClient>(MockBehavior.Strict);
        identityProvider
            .Setup(x => x.FindUserByEmailAsync("new.user@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityProviderUserDto?)null);
        identityProvider
            .Setup(x => x.CreateUserAsync(
                It.Is<CreateIdentityProviderUserRequest>(request =>
                    request.Email == "new.user@example.com"
                    && request.TemporaryPassword == null
                    && request.RequirePasswordUpdate),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(idpUser);
        identityProvider
            .Setup(x => x.GetUserByIdAsync("kc-new-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(idpUser);

        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        userRoles
            .Setup(x => x.ReplaceUserRolesAsync(userId, It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 0), null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        userRoles
            .Setup(x => x.GetRolesForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var versions = new Mock<IUserAccessVersionRepository>(MockBehavior.Strict);
        versions
            .Setup(x => x.GetOrCreateAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserAccessVersion(userId, 1, now));
        versions
            .Setup(x => x.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserAccessVersion(userId, 1, now));

        var operations = new Mock<IUserProvisioningOperationRepository>(MockBehavior.Strict);
        operations
            .Setup(x => x.UpsertAsync(
                It.IsAny<Guid>(),
                "CreateUser",
                "new.user@example.com",
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                null,
                It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string?, string?, Guid?, string, string?, Guid?, CancellationToken>((operationId, type, email, keycloakUserId, platformUserId, status, error, requestedByUserId, _) =>
                Task.FromResult(new UserProvisioningOperation(operationId, type, email, keycloakUserId, platformUserId, status, error, requestedByUserId, now, now)));

        var service = CreateService(
            users.Object,
            userRoles.Object,
            versions.Object,
            identityProvider.Object,
            operations.Object);

        var result = await service.CreateUserAsync(
            new CreateUserRequestDto(
                " new.user@example.com ",
                "New",
                "User",
                "New User",
                Enabled: true,
                TemporaryPassword: " ",
                RequirePasswordUpdate: true,
                []),
            CancellationToken.None);

        result.UserId.Should().Be(userId);
        result.Email.Should().Be("new.user@example.com");
        result.AuthSubject.Should().Be("kc-new-user");

        users.VerifyAll();
        identityProvider.VerifyAll();
        userRoles.VerifyAll();
        versions.VerifyAll();
        operations.VerifyAll();
    }

    [Fact]
    public async Task CreateUserAsync_WhenEmailIsInvalid_ThrowsValidationBeforeIdentityProviderCall()
    {
        var identityProvider = new Mock<IIdentityProviderUserAdminClient>(MockBehavior.Strict);
        var service = CreateService(
            new Mock<IPlatformUserRepository>(MockBehavior.Strict).Object,
            new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict).Object,
            new Mock<IUserAccessVersionRepository>(MockBehavior.Strict).Object,
            identityProvider.Object);

        var act = () => service.CreateUserAsync(
            new CreateUserRequestDto(
                "not-an-email",
                FirstName: null,
                LastName: null,
                DisplayName: null,
                Enabled: true,
                TemporaryPassword: null,
                RequirePasswordUpdate: true,
                []),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("Email");
        identityProvider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateUserAsync_WhenIdentityProviderUserAlreadyExists_UpdatesProfileBeforeProjection()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var existingIdpUser = new IdentityProviderUserDto(
            "kc-existing-user",
            "allo@example.com",
            null,
            null,
            "allo@example.com",
            Enabled: true);
        var updatedIdpUser = existingIdpUser with
        {
            FirstName = "Allo",
            LastName = "Kon",
            DisplayName = "Allo Kon"
        };
        var currentUser = new PlatformUser(
            userId,
            "kc-existing-user",
            "allo@example.com",
            "Allo Kon",
            IsActive: true,
            now,
            now);

        var users = new Mock<IPlatformUserRepository>(MockBehavior.Strict);
        users
            .Setup(x => x.UpsertAsync(
                "kc-existing-user",
                "allo@example.com",
                "Allo Kon",
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(userId);
        users
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentUser);

        var identityProvider = new Mock<IIdentityProviderUserAdminClient>(MockBehavior.Strict);
        identityProvider
            .Setup(x => x.FindUserByEmailAsync("allo@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingIdpUser);
        identityProvider
            .Setup(x => x.UpdateUserAsync(
                "kc-existing-user",
                It.Is<UpdateIdentityProviderUserRequest>(request =>
                    request.Email == "allo@example.com"
                    && request.FirstName == "Allo"
                    && request.LastName == "Kon"
                    && request.DisplayName == "Allo Kon"
                    && request.Enabled),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        identityProvider
            .Setup(x => x.SetTemporaryPasswordAsync(
                "kc-existing-user",
                "Fresh#12345",
                true,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        identityProvider
            .Setup(x => x.GetUserByIdAsync("kc-existing-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedIdpUser);

        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        userRoles
            .Setup(x => x.ReplaceUserRolesAsync(userId, It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 0), null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        userRoles
            .Setup(x => x.GetRolesForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var versions = new Mock<IUserAccessVersionRepository>(MockBehavior.Strict);
        versions
            .Setup(x => x.GetOrCreateAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserAccessVersion(userId, 1, now));
        versions
            .Setup(x => x.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserAccessVersion(userId, 1, now));

        var operations = new Mock<IUserProvisioningOperationRepository>(MockBehavior.Strict);
        operations
            .Setup(x => x.UpsertAsync(
                It.IsAny<Guid>(),
                "CreateUser",
                "allo@example.com",
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                null,
                It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string?, string?, Guid?, string, string?, Guid?, CancellationToken>((operationId, type, email, keycloakUserId, platformUserId, status, error, requestedByUserId, _) =>
                Task.FromResult(new UserProvisioningOperation(operationId, type, email, keycloakUserId, platformUserId, status, error, requestedByUserId, now, now)));

        var service = CreateService(
            users.Object,
            userRoles.Object,
            versions.Object,
            identityProvider.Object,
            operations.Object);

        var result = await service.CreateUserAsync(
            new CreateUserRequestDto(
                "allo@example.com",
                "Allo",
                "Kon",
                "Allo Kon",
                Enabled: true,
                TemporaryPassword: "Fresh#12345",
                RequirePasswordUpdate: true,
                []),
            CancellationToken.None);

        result.FirstName.Should().Be("Allo");
        result.LastName.Should().Be("Kon");
        result.DisplayName.Should().Be("Allo Kon");

        users.VerifyAll();
        identityProvider.VerifyAll();
        userRoles.VerifyAll();
        versions.VerifyAll();
        operations.VerifyAll();
    }

    private static readonly string[] UserIds = ["deleted-kc-user"];
    private static readonly string[] UserEmails = ["allo@example.com"];

    [Fact]
    public async Task GetUsersAsync_WhenIdentityProviderUserWasDeleted_ReturnsKeycloakDisabled()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var platformUser = new PlatformUser(
            userId,
            "deleted-kc-user",
            "allo@example.com",
            "Allo Kon",
            IsActive: true,
            now,
            now);

        var users = new Mock<IPlatformUserRepository>(MockBehavior.Strict);
        users
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([platformUser]);

        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        userRoles
            .Setup(x => x.GetRolesForUsersAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { userId })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<PlatformRole>>());

        var identityProvider = new Mock<IIdentityProviderUserAdminClient>(MockBehavior.Strict);
        identityProvider
            .Setup(x => x.GetUsersByIdsAsync(
                It.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(UserIds)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IdentityProviderUserDto>(StringComparer.Ordinal));
        identityProvider
            .Setup(x => x.FindUsersByEmailsAsync(
                It.Is<IReadOnlyList<string>>(emails => emails.SequenceEqual(UserEmails)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IdentityProviderUserDto>(StringComparer.OrdinalIgnoreCase));

        var service = CreateService(
            users.Object,
            userRoles.Object,
            new Mock<IUserAccessVersionRepository>(MockBehavior.Strict).Object,
            identityProvider.Object);

        var result = await service.GetUsersAsync(CancellationToken.None);

        result.Should().ContainSingle();
        result[0].KeycloakEnabled.Should().BeFalse();

        users.VerifyAll();
        userRoles.VerifyAll();
        identityProvider.VerifyAll();
    }

    [Fact]
    public async Task GetUsersAsync_UsesIdentityProviderBatchLookups()
    {
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        PlatformUser[] platformUsers =
        [
            new(
                firstUserId,
                "kc-first-user",
                "first@example.com",
                "First User",
                IsActive: true,
                now,
                now),
            new(
                secondUserId,
                "kc-second-user",
                "second@example.com",
                "Second User",
                IsActive: true,
                now,
                now)
        ];

        var users = new Mock<IPlatformUserRepository>(MockBehavior.Strict);
        users
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(platformUsers);

        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        userRoles
            .Setup(x => x.GetRolesForUsersAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { firstUserId, secondUserId })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<PlatformRole>>());

        var identityProvider = new Mock<IIdentityProviderUserAdminClient>(MockBehavior.Strict);
        identityProvider
            .Setup(x => x.GetUsersByIdsAsync(
                It.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { "kc-first-user", "kc-second-user" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IdentityProviderUserDto>(StringComparer.Ordinal)
            {
                ["kc-first-user"] = new("kc-first-user", "first@example.com", null, null, "First User", Enabled: true)
            });
        identityProvider
            .Setup(x => x.FindUsersByEmailsAsync(
                It.Is<IReadOnlyList<string>>(emails => emails.SequenceEqual(new[] { "second@example.com" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IdentityProviderUserDto>(StringComparer.OrdinalIgnoreCase)
            {
                ["second@example.com"] = new("kc-second-user-rebound", "second@example.com", null, null, "Second User", Enabled: false)
            });

        var service = CreateService(
            users.Object,
            userRoles.Object,
            new Mock<IUserAccessVersionRepository>(MockBehavior.Strict).Object,
            identityProvider.Object);

        var result = await service.GetUsersAsync(CancellationToken.None);

        result.Should().HaveCount(2);
        result.Single(x => x.UserId == firstUserId).KeycloakEnabled.Should().BeTrue();
        result.Single(x => x.UserId == secondUserId).KeycloakEnabled.Should().BeFalse();

        identityProvider.Verify(x => x.GetUserByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        identityProvider.Verify(x => x.FindUserByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        users.VerifyAll();
        userRoles.VerifyAll();
        identityProvider.VerifyAll();
    }

    [Fact]
    public async Task GetUserAsync_WhenIdentityProviderDisplayNameIsEmail_ReturnsStoredApplicationDisplayName()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var platformUser = new PlatformUser(
            userId,
            "kc-user",
            "pm-tester@example.com",
            "Tester",
            IsActive: true,
            now,
            now);
        var idpUser = new IdentityProviderUserDto(
            "kc-user",
            "pm-tester@example.com",
            null,
            null,
            "pm-tester@example.com",
            Enabled: true);

        var users = new Mock<IPlatformUserRepository>(MockBehavior.Strict);
        users
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(platformUser);

        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        userRoles
            .Setup(x => x.GetRolesForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var versions = new Mock<IUserAccessVersionRepository>(MockBehavior.Strict);
        versions
            .Setup(x => x.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserAccessVersion(userId, 3, now));

        var identityProvider = new Mock<IIdentityProviderUserAdminClient>(MockBehavior.Strict);
        identityProvider
            .Setup(x => x.GetUserByIdAsync("kc-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(idpUser);

        var service = CreateService(
            users.Object,
            userRoles.Object,
            versions.Object,
            identityProvider.Object);

        var result = await service.GetUserAsync(userId, CancellationToken.None);

        result.DisplayName.Should().Be("Tester");
        result.Email.Should().Be("pm-tester@example.com");

        users.VerifyAll();
        userRoles.VerifyAll();
        versions.VerifyAll();
        identityProvider.VerifyAll();
    }

    [Fact]
    public async Task UpdateUserAsync_WhenChangingPasswordWithoutNames_PreservesIdentityProviderNames()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var currentUser = new PlatformUser(
            userId,
            "kc-user",
            "resident@example.com",
            "Resident User",
            IsActive: true,
            now,
            now);

        var idpUser = new IdentityProviderUserDto(
            "kc-user",
            "resident@example.com",
            "Resident",
            "User",
            "Resident User",
            Enabled: true);

        var users = new Mock<IPlatformUserRepository>(MockBehavior.Strict);
        users
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentUser);
        users
            .Setup(x => x.UpsertAsync(
                "kc-user",
                "resident@example.com",
                "Resident User",
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(userId);

        var identityProvider = new Mock<IIdentityProviderUserAdminClient>(MockBehavior.Strict);
        identityProvider
            .Setup(x => x.GetUserByIdAsync("kc-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(idpUser);
        identityProvider
            .Setup(x => x.UpdateUserAsync(
                "kc-user",
                It.Is<UpdateIdentityProviderUserRequest>(request =>
                    request.Email == "resident@example.com"
                    && request.FirstName == "Resident"
                    && request.LastName == "User"
                    && request.DisplayName == "Resident User"
                    && request.Enabled),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        identityProvider
            .Setup(x => x.SetTemporaryPasswordAsync(
                "kc-user",
                "Fresh#12345",
                true,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        userRoles
            .Setup(x => x.ReplaceUserRolesAsync(userId, It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 0), null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        userRoles
            .Setup(x => x.GetRolesForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var versions = new Mock<IUserAccessVersionRepository>(MockBehavior.Strict);
        versions
            .Setup(x => x.IncrementAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserAccessVersion(userId, 2, now));
        versions
            .Setup(x => x.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserAccessVersion(userId, 2, now));

        var service = CreateService(
            users.Object,
            userRoles.Object,
            versions.Object,
            identityProvider.Object);

        var result = await service.UpdateUserAsync(
            userId,
            new UpdateUserRequestDto(
                "resident@example.com",
                FirstName: null,
                LastName: null,
                DisplayName: "Resident User",
                Enabled: true,
                TemporaryPassword: "Fresh#12345",
                RequirePasswordUpdate: true,
                []),
            CancellationToken.None);

        result.FirstName.Should().Be("Resident");
        result.LastName.Should().Be("User");

        users.VerifyAll();
        identityProvider.VerifyAll();
        userRoles.VerifyAll();
        versions.VerifyAll();
    }

    [Fact]
    public async Task UpdateUserAsync_WhenStoredIdentityProviderSubjectIsMissing_RebindsByEmailBeforeSaving()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var currentUser = new PlatformUser(
            userId,
            "stale-kc-user",
            "clerk@example.com",
            "Clerk",
            IsActive: true,
            now,
            now);

        var idpUser = new IdentityProviderUserDto(
            "actual-kc-user",
            "clerk@example.com",
            "Payables",
            "Clerk",
            "Payables Clerk",
            Enabled: true);

        var users = new Mock<IPlatformUserRepository>(MockBehavior.Strict);
        users
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => currentUser);
        users
            .Setup(x => x.UpsertAsync(
                "actual-kc-user",
                "clerk@example.com",
                "Payables Clerk",
                true,
                It.IsAny<CancellationToken>()))
            .Callback<string, string?, string?, bool, CancellationToken>((authSubject, email, displayName, isActive, _) =>
            {
                currentUser = currentUser with
                {
                    AuthSubject = authSubject,
                    Email = email,
                    DisplayName = displayName,
                    IsActive = isActive,
                    UpdatedAtUtc = DateTime.UtcNow
                };
            })
            .ReturnsAsync(userId);

        var identityProvider = new Mock<IIdentityProviderUserAdminClient>(MockBehavior.Strict);
        identityProvider
            .Setup(x => x.GetUserByIdAsync("stale-kc-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityProviderUserDto?)null);
        identityProvider
            .Setup(x => x.FindUserByEmailAsync("clerk@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(idpUser);
        identityProvider
            .Setup(x => x.UpdateUserAsync(
                "actual-kc-user",
                It.Is<UpdateIdentityProviderUserRequest>(request =>
                    request.Email == "clerk@example.com"
                    && request.FirstName == "Payables"
                    && request.LastName == "Clerk"
                    && request.DisplayName == "Payables Clerk"
                    && request.Enabled),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        identityProvider
            .Setup(x => x.SetTemporaryPasswordAsync(
                "actual-kc-user",
                "Fresh#12345",
                true,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        identityProvider
            .Setup(x => x.GetUserByIdAsync("actual-kc-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(idpUser);

        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        userRoles
            .Setup(x => x.ReplaceUserRolesAsync(userId, It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { roleId })), null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        userRoles
            .Setup(x => x.GetRolesForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new PlatformRole(roleId, "pm-ap-clerk", "PM AP Clerk", null, IsSystem: true, IsActive: true, now, now)
            ]);

        var versions = new Mock<IUserAccessVersionRepository>(MockBehavior.Strict);
        versions
            .Setup(x => x.IncrementAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserAccessVersion(userId, 2, now));
        versions
            .Setup(x => x.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserAccessVersion(userId, 2, now));

        var service = CreateService(
            users.Object,
            userRoles.Object,
            versions.Object,
            identityProvider.Object);

        var result = await service.UpdateUserAsync(
            userId,
            new UpdateUserRequestDto(
                "clerk@example.com",
                "Payables",
                "Clerk",
                "Payables Clerk",
                Enabled: true,
                TemporaryPassword: "Fresh#12345",
                RequirePasswordUpdate: true,
                [roleId]),
            CancellationToken.None);

        result.AuthSubject.Should().Be("actual-kc-user");
        result.Email.Should().Be("clerk@example.com");
        result.KeycloakEnabled.Should().BeTrue();
        result.Roles.Should().ContainSingle(role => role.Code == "pm-ap-clerk");

        identityProvider.Verify(x => x.UpdateUserAsync(
            "stale-kc-user",
            It.IsAny<UpdateIdentityProviderUserRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
        users.VerifyAll();
        identityProvider.VerifyAll();
        userRoles.VerifyAll();
        versions.VerifyAll();
    }

    [Fact]
    public async Task UpdateUserAsync_WhenIdentityProviderUserIsMissing_ProvisionsUserAndRebindsBeforeSaving()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var currentUser = new PlatformUser(
            userId,
            "stale-kc-user",
            "clerk@example.com",
            "Clerk",
            IsActive: true,
            now,
            now);

        var createdIdpUser = new IdentityProviderUserDto(
            "new-kc-user",
            "clerk@example.com",
            "Payables",
            "Clerk",
            "Payables Clerk",
            Enabled: true);

        var users = new Mock<IPlatformUserRepository>(MockBehavior.Strict);
        users
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => currentUser);
        users
            .Setup(x => x.UpsertAsync(
                "new-kc-user",
                "clerk@example.com",
                "Payables Clerk",
                true,
                It.IsAny<CancellationToken>()))
            .Callback<string, string?, string?, bool, CancellationToken>((authSubject, email, displayName, isActive, _) =>
            {
                currentUser = currentUser with
                {
                    AuthSubject = authSubject,
                    Email = email,
                    DisplayName = displayName,
                    IsActive = isActive,
                    UpdatedAtUtc = DateTime.UtcNow
                };
            })
            .ReturnsAsync(userId);

        var identityProvider = new Mock<IIdentityProviderUserAdminClient>(MockBehavior.Strict);
        identityProvider
            .Setup(x => x.GetUserByIdAsync("stale-kc-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityProviderUserDto?)null);
        identityProvider
            .Setup(x => x.FindUserByEmailAsync("clerk@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityProviderUserDto?)null);
        identityProvider
            .Setup(x => x.CreateUserAsync(
                It.Is<CreateIdentityProviderUserRequest>(request =>
                    request.Email == "clerk@example.com"
                    && request.FirstName == "Payables"
                    && request.LastName == "Clerk"
                    && request.DisplayName == "Payables Clerk"
                    && request.Enabled
                    && request.TemporaryPassword == null
                    && !request.RequirePasswordUpdate),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdIdpUser);
        identityProvider
            .Setup(x => x.UpdateUserAsync(
                "new-kc-user",
                It.Is<UpdateIdentityProviderUserRequest>(request =>
                    request.Email == "clerk@example.com"
                    && request.DisplayName == "Payables Clerk"
                    && request.Enabled),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        identityProvider
            .Setup(x => x.GetUserByIdAsync("new-kc-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdIdpUser);

        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        userRoles
            .Setup(x => x.ReplaceUserRolesAsync(userId, It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { roleId })), null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        userRoles
            .Setup(x => x.GetRolesForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new PlatformRole(roleId, "pm-ap-clerk", "PM AP Clerk", null, IsSystem: true, IsActive: true, now, now)
            ]);

        var versions = new Mock<IUserAccessVersionRepository>(MockBehavior.Strict);
        versions
            .Setup(x => x.IncrementAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserAccessVersion(userId, 2, now));
        versions
            .Setup(x => x.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserAccessVersion(userId, 2, now));

        var service = CreateService(
            users.Object,
            userRoles.Object,
            versions.Object,
            identityProvider.Object);

        var result = await service.UpdateUserAsync(
            userId,
            new UpdateUserRequestDto(
                "clerk@example.com",
                "Payables",
                "Clerk",
                "Payables Clerk",
                Enabled: true,
                TemporaryPassword: null,
                RequirePasswordUpdate: false,
                [roleId]),
            CancellationToken.None);

        result.AuthSubject.Should().Be("new-kc-user");
        result.Email.Should().Be("clerk@example.com");
        result.Roles.Should().ContainSingle(role => role.Code == "pm-ap-clerk");

        users.VerifyAll();
        identityProvider.VerifyAll();
        userRoles.VerifyAll();
        versions.VerifyAll();
    }

    [Fact]
    public async Task ReplaceUserRolesAsync_WritesUserAndRoleAssignmentAuditChanges()
    {
        var userId = Guid.NewGuid();
        var oldRoleId = Guid.NewGuid();
        var newRoleId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var currentUser = new PlatformUser(
            userId,
            "kc-user",
            "clerk@example.com",
            "Clerk User",
            IsActive: true,
            now,
            now);
        var oldRole = new PlatformRole(oldRoleId, "pm-ap-clerk", "PM AP Clerk", null, IsSystem: true, IsActive: true, now, now);
        var newRole = new PlatformRole(newRoleId, "pm-ar-clerk", "PM AR Clerk", null, IsSystem: true, IsActive: true, now, now);

        var users = new Mock<IPlatformUserRepository>(MockBehavior.Strict);
        users
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentUser);

        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        userRoles
            .SetupSequence(x => x.GetRolesForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([oldRole])
            .ReturnsAsync([newRole]);
        userRoles
            .Setup(x => x.ReplaceUserRolesAsync(
                userId,
                It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { newRoleId })),
                null,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var versions = new Mock<IUserAccessVersionRepository>(MockBehavior.Strict);
        versions
            .Setup(x => x.IncrementAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserAccessVersion(userId, 2, now));

        var auditCalls = new List<AuditCall>();
        var audit = new Mock<IAuditLogService>(MockBehavior.Strict);
        audit
            .Setup(x => x.WriteAsync(
                It.IsAny<AuditEntityKind>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<AuditFieldChange>?>(),
                It.IsAny<object?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<AuditEntityKind, Guid, string, IReadOnlyList<AuditFieldChange>?, object?, Guid?, CancellationToken>((kind, entityId, actionCode, changes, _, _, _) =>
                auditCalls.Add(new AuditCall(kind, entityId, actionCode, changes ?? Array.Empty<AuditFieldChange>())))
            .Returns(Task.CompletedTask);

        var service = CreateService(
            users.Object,
            userRoles.Object,
            versions.Object,
            new Mock<IIdentityProviderUserAdminClient>(MockBehavior.Strict).Object,
            audit: audit.Object);

        await service.ReplaceUserRolesAsync(
            userId,
            new ReplaceUserRolesRequestDto([newRoleId]),
            CancellationToken.None);

        auditCalls.Should().HaveCount(3);

        var userAudit = auditCalls.Should().ContainSingle(x =>
            x.EntityKind == AuditEntityKind.SecurityUser
            && x.EntityId == userId
            && x.ActionCode == AuditActionCodes.SecurityUserRolesReplace).Subject;
        userAudit.Changes.Should().ContainSingle(x => x.FieldPath == "roles");
        userAudit.Changes[0].OldValueJson.Should().Contain("PM AP Clerk");
        userAudit.Changes[0].NewValueJson.Should().Contain("PM AR Clerk");

        var removedRoleAudit = auditCalls.Should().ContainSingle(x =>
            x.EntityKind == AuditEntityKind.SecurityRole
            && x.EntityId == oldRoleId
            && x.ActionCode == AuditActionCodes.SecurityRoleUpdate).Subject;
        removedRoleAudit.Changes.Should().ContainSingle(x => x.FieldPath == "assigned_users");
        removedRoleAudit.Changes[0].OldValueJson.Should().Contain("clerk@example.com");
        removedRoleAudit.Changes[0].NewValueJson.Should().BeNull();

        var addedRoleAudit = auditCalls.Should().ContainSingle(x =>
            x.EntityKind == AuditEntityKind.SecurityRole
            && x.EntityId == newRoleId
            && x.ActionCode == AuditActionCodes.SecurityRoleUpdate).Subject;
        addedRoleAudit.Changes.Should().ContainSingle(x => x.FieldPath == "assigned_users");
        addedRoleAudit.Changes[0].OldValueJson.Should().BeNull();
        addedRoleAudit.Changes[0].NewValueJson.Should().Contain("clerk@example.com");

        users.VerifyAll();
        userRoles.VerifyAll();
        versions.VerifyAll();
        audit.VerifyAll();
    }

    [Fact]
    public async Task DeactivateUserAsync_WhenStoredIdentityProviderSubjectIsMissing_RebindsByEmailBeforeDisabling()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var currentUser = new PlatformUser(
            userId,
            "stale-kc-user",
            "clerk@example.com",
            "Payables Clerk",
            IsActive: true,
            now,
            now);

        var idpUser = new IdentityProviderUserDto(
            "actual-kc-user",
            "clerk@example.com",
            "Payables",
            "Clerk",
            "Payables Clerk",
            Enabled: true);

        var users = new Mock<IPlatformUserRepository>(MockBehavior.Strict);
        users
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentUser);
        users
            .Setup(x => x.UpsertAsync(
                "actual-kc-user",
                "clerk@example.com",
                "Payables Clerk",
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(userId);

        var identityProvider = new Mock<IIdentityProviderUserAdminClient>(MockBehavior.Strict);
        identityProvider
            .Setup(x => x.GetUserByIdAsync("stale-kc-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityProviderUserDto?)null);
        identityProvider
            .Setup(x => x.FindUserByEmailAsync("clerk@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(idpUser);
        identityProvider
            .Setup(x => x.SetUserEnabledAsync("actual-kc-user", false, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var versions = new Mock<IUserAccessVersionRepository>(MockBehavior.Strict);
        versions
            .Setup(x => x.IncrementAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserAccessVersion(userId, 2, now));

        var service = CreateService(
            users.Object,
            new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict).Object,
            versions.Object,
            identityProvider.Object);

        await service.DeactivateUserAsync(userId, CancellationToken.None);

        identityProvider.Verify(x => x.SetUserEnabledAsync("stale-kc-user", false, It.IsAny<CancellationToken>()), Times.Never);
        users.VerifyAll();
        identityProvider.VerifyAll();
        versions.VerifyAll();
    }

    private static UserAccessManagementService CreateService(
        IPlatformUserRepository users,
        IPlatformUserRoleRepository userRoles,
        IUserAccessVersionRepository versions,
        IIdentityProviderUserAdminClient identityProvider,
        IUserProvisioningOperationRepository? operations = null,
        IAuditLogService? audit = null)
    {
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var defaultAudit = new Mock<IAuditLogService>(MockBehavior.Strict);
        defaultAudit
            .Setup(x => x.WriteAsync(
                It.IsAny<AuditEntityKind>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<AuditFieldChange>?>(),
                It.IsAny<object?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new UserAccessManagementService(
            uow.Object,
            users,
            userRoles,
            versions,
            operations ?? new Mock<IUserProvisioningOperationRepository>(MockBehavior.Strict).Object,
            identityProvider,
            audit ?? defaultAudit.Object);
    }

    private sealed record AuditCall(
        AuditEntityKind EntityKind,
        Guid EntityId,
        string ActionCode,
        IReadOnlyList<AuditFieldChange> Changes);
}
