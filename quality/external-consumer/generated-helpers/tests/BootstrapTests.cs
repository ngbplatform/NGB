using Moq;
using NGB.Core.Security;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NgbApplication;
using NgbApplication.Api;
using NgbApplication.Migrator;
using Xunit;

namespace GeneratedHelpers.Tests;

public sealed class BootstrapTests
{
    [Fact]
    public async Task MissingAdministratorIsCreatedInsideTransaction()
    {
        var roles = new Mock<IPlatformRoleRepository>();
        var transaction = new Mock<IUnitOfWork>();
        using var cancellation = new CancellationTokenSource();

        await AdministratorBootstrap.EnsureAsync(roles.Object, transaction.Object, cancellation.Token);

        roles.Verify(repository => repository.UpsertAsync(
            Guid.Parse("019a0c12-8000-7000-8000-000000000001"), ApplicationRoles.Administrator,
            "Administrator", "Full application access for active users.", true, true, cancellation.Token), Times.Once);
        transaction.Verify(value => value.BeginTransactionAsync(cancellation.Token), Times.Once);
        transaction.Verify(value => value.CommitAsync(cancellation.Token), Times.Once);
        transaction.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExistingRoleIsNotRenamedReactivatedOrOverwritten(bool active)
    {
        var roles = new Mock<IPlatformRoleRepository>(MockBehavior.Strict);
        roles.Setup(repository => repository.GetByCodeAsync(ApplicationRoles.Administrator, CancellationToken.None))
            .ReturnsAsync(new PlatformRole(Guid.NewGuid(), ApplicationRoles.Administrator, "Owner", "Keep",
                false, active, DateTime.UnixEpoch, DateTime.UnixEpoch));
        var transaction = new Mock<IUnitOfWork>();

        await AdministratorBootstrap.EnsureAsync(roles.Object, transaction.Object, CancellationToken.None);

        roles.VerifyAll();
        roles.VerifyNoOtherCalls();
        transaction.Verify(value => value.CommitAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task FailedSeedRollsBackAndPropagatesTheFailure()
    {
        var roles = new Mock<IPlatformRoleRepository>();
        roles.Setup(repository => repository.GetByCodeAsync(ApplicationRoles.Administrator, CancellationToken.None))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));
        var transaction = new Mock<IUnitOfWork>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AdministratorBootstrap.EnsureAsync(roles.Object, transaction.Object, CancellationToken.None));

        transaction.Verify(value => value.RollbackAsync(CancellationToken.None), Times.Once);
        transaction.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StarterMenuExposesOnlyRealPlatformPages()
    {
        var groups = await new ApplicationMenu().ContributeAsync(CancellationToken.None);
        var group = Assert.Single(groups);
        Assert.Equal("Administration", group.Label);
        Assert.Equal(["/admin/security/users", "/admin/security/roles"], group.Items.Select(item => item.Route));
    }
}
