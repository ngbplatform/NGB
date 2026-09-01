using FluentAssertions;
using Moq;
using NGB.BackgroundJobs.Hosting;
using NGB.Persistence.Databases;
using NGB.Tools.Exceptions;

namespace NGB.BackgroundJobs.Tests.Infrastructure;

public sealed class BackgroundJobsHostingBootstrapFullCoverageTests
{
    [Fact]
    public async Task HostingBootstrap_NormalizesValuesAndDelegatesToProviderBoundary()
    {
        var options = new BackgroundJobsHostingOptions();
        var provisioner = new Mock<IDatabaseProvisioner>(MockBehavior.Strict);
        provisioner.Setup(x => x.EnsureDatabaseExistsAsync("jobs", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var bootstrap = new BackgroundJobsHostingBootstrap(options, " app ", " jobs ");

        bootstrap.Options.Should().BeSameAs(options);
        bootstrap.ApplicationConnectionString.Should().Be("app");
        bootstrap.HangfireConnectionString.Should().Be("jobs");

        await bootstrap.EnsureInfrastructureAsync(provisioner.Object);

        provisioner.VerifyAll();
    }

    [Fact]
    public async Task HostingBootstrap_RejectsEveryMissingDependency()
    {
        Action missingOptions = () => new BackgroundJobsHostingBootstrap(null!, "app", "jobs");
        Action missingApplication = () => new BackgroundJobsHostingBootstrap(new(), " ", "jobs");
        Action missingHangfire = () => new BackgroundJobsHostingBootstrap(new(), "app", " ");
        var bootstrap = new BackgroundJobsHostingBootstrap(new(), "app", "jobs");
        Func<Task> missingProvisioner = () => bootstrap.EnsureInfrastructureAsync(null!);

        missingOptions.Should().Throw<NgbArgumentRequiredException>();
        missingApplication.Should().Throw<NgbArgumentRequiredException>();
        missingHangfire.Should().Throw<NgbArgumentRequiredException>();
        await missingProvisioner.Should().ThrowAsync<NgbArgumentRequiredException>();
    }
}
