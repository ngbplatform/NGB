using FluentAssertions;
using Moq;
using NGB.Core.AuditLog;
using NGB.Core.Security;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.PostgreSql.Bootstrap;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Bootstrap;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PropertyManagementSecuritySeederEnvironmentCollection
{
    public const string Name = "Property Management security seeder environment";
}

[Collection(PropertyManagementSecuritySeederEnvironmentCollection.Name)]
public sealed class PropertyManagementSecuritySeederFullCoverageTests
{
    private const string AdministratorRoleCode = "pm-administrator";

    [Fact]
    public async Task EnsureSeededAsync_WithSubjectAndMatchingEmail_UpsertsUserAndAssignsMissingRole()
    {
        var fixture = new SeederFixture();
        var administratorRole = Role(AdministratorRoleCode);
        var existingRole = Role("pm-accountant");
        var upsertedUserId = Guid.CreateVersion7();
        var emailMatchedUserId = Guid.CreateVersion7();

        fixture.Roles.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([administratorRole]);
        fixture.Roles.Setup(x => x.GetByCodeAsync(AdministratorRoleCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(administratorRole);
        fixture.Users.Setup(x => x.UpsertAsync(
                "keycloak-admin",
                "admin@example.com",
                "Admin User",
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(upsertedUserId);
        fixture.Users.Setup(x => x.GetByEmailsAsync(
                It.Is<IReadOnlyList<string>>(emails => emails.SequenceEqual(new[] { "admin@example.com" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                User(upsertedUserId, "ADMIN@example.com"),
                User(emailMatchedUserId, "admin@example.com")
            ]);
        fixture.UserRoles.Setup(x => x.GetRolesForUserAsync(upsertedUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([administratorRole]);
        fixture.UserRoles.Setup(x => x.GetRolesForUserAsync(emailMatchedUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([existingRole]);
        fixture.Versions.Setup(x => x.GetAsync(emailMatchedUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUserAccessVersion?)null);

        using var environment = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["KEYCLOAK_DEMO_ADMIN_EMAIL"] = " admin@example.com ",
            ["KEYCLOAK_DEMO_ADMIN_ID"] = " keycloak-admin ",
            ["KEYCLOAK_DEMO_ADMIN_FIRST_NAME"] = " Admin ",
            ["KEYCLOAK_DEMO_ADMIN_LAST_NAME"] = " User "
        });

        await fixture.Sut.EnsureSeededAsync();

        fixture.UserRoles.Verify(x => x.ReplaceUserRolesAsync(
            emailMatchedUserId,
            It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 2
                && ids.Contains(existingRole.RoleId)
                && ids.Contains(administratorRole.RoleId)),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Versions.Verify(x => x.GetOrCreateAsync(emailMatchedUserId, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Versions.Verify(x => x.IncrementAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnsureSeededAsync_WithBlankDisplayName_UsesEmailAndIncrementsExistingAccessVersion()
    {
        var fixture = new SeederFixture();
        var administratorRole = Role(AdministratorRoleCode);
        var userId = Guid.CreateVersion7();

        fixture.Roles.Setup(x => x.GetByCodeAsync(AdministratorRoleCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(administratorRole);
        fixture.Users.Setup(x => x.UpsertAsync(
                "keycloak-admin",
                "admin@example.com",
                "admin@example.com",
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(userId);
        fixture.Users.Setup(x => x.GetByEmailsAsync(
                It.Is<IReadOnlyList<string>>(emails => emails.SequenceEqual(new[] { "admin@example.com" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        fixture.UserRoles.Setup(x => x.GetRolesForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        fixture.Versions.Setup(x => x.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserAccessVersion(userId, 7, DateTime.UtcNow));

        using var environment = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["KEYCLOAK_DEMO_ADMIN_EMAIL"] = "admin@example.com",
            ["KEYCLOAK_DEMO_ADMIN_ID"] = "keycloak-admin",
            ["KEYCLOAK_DEMO_ADMIN_FIRST_NAME"] = " ",
            ["KEYCLOAK_DEMO_ADMIN_LAST_NAME"] = null
        });

        await fixture.Sut.EnsureSeededAsync();

        fixture.Versions.Verify(x => x.IncrementAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
        fixture.UserRoles.Verify(x => x.ReplaceUserRolesAsync(
            userId,
            It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { administratorRole.RoleId })),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureSeededAsync_WithoutAdministratorRole_SkipsDemoUser()
    {
        var fixture = new SeederFixture();
        fixture.Roles.Setup(x => x.GetByCodeAsync(AdministratorRoleCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformRole?)null);

        await fixture.Sut.EnsureSeededAsync();

        fixture.Users.Verify(x => x.UpsertAsync(
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.Users.Verify(x => x.GetByEmailsAsync(
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnsureSeededAsync_WithoutDemoIdentity_CommitsRoleDefaultsAndStops()
    {
        var fixture = new SeederFixture();
        fixture.Roles.Setup(x => x.GetByCodeAsync(AdministratorRoleCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Role(AdministratorRoleCode));
        using var environment = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["KEYCLOAK_DEMO_ADMIN_EMAIL"] = null,
            ["KEYCLOAK_DEMO_ADMIN_ID"] = null,
            ["KEYCLOAK_DEMO_ADMIN_FIRST_NAME"] = null,
            ["KEYCLOAK_DEMO_ADMIN_LAST_NAME"] = null
        });

        await fixture.Sut.EnsureSeededAsync();

        fixture.Uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.UserRoles.Verify(x => x.GetRolesForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnsureSeededAsync_WhenRoleUpsertFails_RollsBackAndRethrows()
    {
        var fixture = new SeederFixture();
        fixture.Roles.Setup(x => x.UpsertAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                true,
                true,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("role failure"));

        var act = () => fixture.Sut.EnsureSeededAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("role failure");
        fixture.Uow.Verify(x => x.RollbackAsync(CancellationToken.None), Times.Once);
        fixture.Uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnsureSeededAsync_WhenDemoUserUpsertFails_RollsBackAndRethrows()
    {
        var fixture = new SeederFixture();
        fixture.Roles.Setup(x => x.GetByCodeAsync(AdministratorRoleCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Role(AdministratorRoleCode));
        fixture.Users.Setup(x => x.UpsertAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                true,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("user failure"));
        using var environment = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["KEYCLOAK_DEMO_ADMIN_EMAIL"] = null,
            ["KEYCLOAK_DEMO_ADMIN_ID"] = "keycloak-admin",
            ["KEYCLOAK_DEMO_ADMIN_FIRST_NAME"] = null,
            ["KEYCLOAK_DEMO_ADMIN_LAST_NAME"] = null
        });

        var act = () => fixture.Sut.EnsureSeededAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("user failure");
        fixture.Uow.Verify(x => x.RollbackAsync(CancellationToken.None), Times.Once);
        fixture.Uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureSeededAsync_WhenRoleAssignmentFails_RollsBackAndRethrows()
    {
        var fixture = new SeederFixture();
        var administratorRole = Role(AdministratorRoleCode);
        var userId = Guid.CreateVersion7();
        fixture.Roles.Setup(x => x.GetByCodeAsync(AdministratorRoleCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(administratorRole);
        fixture.Users.Setup(x => x.UpsertAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(userId);
        fixture.UserRoles.Setup(x => x.GetRolesForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        fixture.UserRoles.Setup(x => x.ReplaceUserRolesAsync(
                userId,
                It.IsAny<IReadOnlyList<Guid>>(),
                null,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("assignment failure"));
        using var environment = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["KEYCLOAK_DEMO_ADMIN_EMAIL"] = null,
            ["KEYCLOAK_DEMO_ADMIN_ID"] = "keycloak-admin",
            ["KEYCLOAK_DEMO_ADMIN_FIRST_NAME"] = null,
            ["KEYCLOAK_DEMO_ADMIN_LAST_NAME"] = null
        });

        var act = () => fixture.Sut.EnsureSeededAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("assignment failure");
        fixture.Uow.Verify(x => x.RollbackAsync(CancellationToken.None), Times.Once);
        fixture.Uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static PlatformRole Role(string code) => new(
        Guid.CreateVersion7(),
        code,
        code,
        null,
        true,
        true,
        DateTime.UtcNow,
        DateTime.UtcNow);

    private static PlatformUser User(Guid userId, string email) => new(
        userId,
        $"auth-{userId:N}",
        email,
        email,
        true,
        DateTime.UtcNow,
        DateTime.UtcNow);

    private sealed class SeederFixture
    {
        public Mock<IUnitOfWork> Uow { get; } = new(MockBehavior.Loose);
        public Mock<IPlatformUserRepository> Users { get; } = new(MockBehavior.Loose);
        public Mock<IPlatformRoleRepository> Roles { get; } = new(MockBehavior.Loose);
        public Mock<IPlatformUserRoleRepository> UserRoles { get; } = new(MockBehavior.Loose);
        public Mock<IUserAccessVersionRepository> Versions { get; } = new(MockBehavior.Loose);
        public Mock<IPermissionSnapshotRepository> Permissions { get; } = new(MockBehavior.Loose);

        public PropertyManagementSecuritySeeder Sut { get; }

        public SeederFixture()
        {
            Uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Roles.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Roles.Setup(x => x.UpsertAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    true,
                    true,
                    It.IsAny<CancellationToken>()))
                .Returns((Guid roleId, string code, string name, string? description, bool isSystem, bool isActive, CancellationToken _) =>
                    Task.FromResult(new PlatformRole(
                        roleId,
                        code,
                        name,
                        description,
                        isSystem,
                        isActive,
                        DateTime.UtcNow,
                        DateTime.UtcNow)));
            Permissions.Setup(x => x.ReplaceRolePermissionsAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<IReadOnlyList<NgbPermissionKey>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Versions.Setup(x => x.GetOrCreateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid userId, CancellationToken _) =>
                    new PlatformUserAccessVersion(userId, 1, DateTime.UtcNow));
            Versions.Setup(x => x.IncrementAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid userId, CancellationToken _) =>
                    new PlatformUserAccessVersion(userId, 2, DateTime.UtcNow));

            Sut = new PropertyManagementSecuritySeeder(
                Uow.Object,
                Users.Object,
                Roles.Object,
                UserRoles.Object,
                Versions.Object,
                Permissions.Object);
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _previousValues = new(StringComparer.Ordinal);

        public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> values)
        {
            foreach (var pair in values)
            {
                _previousValues[pair.Key] = Environment.GetEnvironmentVariable(pair.Key);
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }

        public void Dispose()
        {
            foreach (var pair in _previousValues)
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
}
