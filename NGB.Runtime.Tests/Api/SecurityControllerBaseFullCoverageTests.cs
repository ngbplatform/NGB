using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NGB.Api.Controllers;
using NGB.Contracts.Security;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.Runtime.Tests.Api;

public sealed class SecurityControllerBaseFullCoverageTests
{
    [Fact]
    public async Task Previously_uncovered_user_and_role_operations_require_permission_and_delegate()
    {
        var users = new Mock<IUserAccessManagementService>(MockBehavior.Strict);
        var roles = new Mock<IRoleManagementService>(MockBehavior.Strict);
        var access = new Mock<INgbAccessChecker>(MockBehavior.Strict);
        access.Setup(x => x.RequireAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var userRequest = new UpdateUserRequestDto(null, null, null, null, true, null, false, []);
        var roleRequest = new UpdateRoleRequestDto("role", "Role", null, true, []);
        users.Setup(x => x.UpdateUserAsync(userId, userRequest, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserDetailsDto)null!);
        users.Setup(x => x.ReactivateUserAsync(userId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        users.Setup(x => x.GetUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<UserListItemDto>());
        roles.Setup(x => x.UpdateRoleAsync(roleId, roleRequest, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoleDetailsDto)null!);
        var sut = Controller(users.Object, roles.Object, access.Object);

        (await sut.UpdateUser(userId, userRequest, default)).Should().BeNull();
        (await sut.ReactivateUser(userId, default)).Should().BeOfType<NoContentResult>();
        (await sut.GetUsers(default)).Should().BeEmpty();
        (await sut.UpdateRole(roleId, roleRequest, default)).Should().BeNull();

        users.VerifyAll();
        roles.VerifyAll();
        access.Verify(x => x.RequireAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

    private static TestSecurityController Controller(
        IUserAccessManagementService users,
        IRoleManagementService roles,
        INgbAccessChecker access)
        => new(
            Mock.Of<ICurrentAccessService>(),
            new PermissionDefinitionRegistry([]),
            users,
            roles,
            Mock.Of<IEffectiveAccessService>(),
            access);

    private sealed class TestSecurityController(
        ICurrentAccessService currentAccess,
        PermissionDefinitionRegistry permissionDefinitions,
        IUserAccessManagementService users,
        IRoleManagementService roles,
        IEffectiveAccessService effectiveAccess,
        INgbAccessChecker access)
        : SecurityControllerBase(currentAccess, permissionDefinitions, users, roles, effectiveAccess, access);
}
