using FluentAssertions;
using NGB.BackgroundJobs.Catalog;
using NGB.Tools.Exceptions;

namespace NGB.BackgroundJobs.Tests.Catalog;

public sealed class BackgroundJobCatalog_P0Tests
{
    [Fact]
    public void Ctor_AggregatesUniqueJobIds_InContributorOrder()
    {
        var catalog = new BackgroundJobCatalog(
        [
            new StubContributor("job.alpha", "job.beta"),
            new StubContributor("job.gamma")
        ]);

        catalog.All.Should().Equal("job.alpha", "job.beta", "job.gamma");
    }

    [Fact]
    public void Ctor_WhenDuplicateJobIdExists_ThrowsConfigurationViolation()
    {
        var act = () => new BackgroundJobCatalog(
        [
            new StubContributor("job.alpha"),
            new StubContributor("job.alpha")
        ]);

        var ex = act.Should().Throw<NgbConfigurationViolationException>().Which;
        ex.ErrorCode.Should().Be(NgbConfigurationViolationException.Code);
    }

    private sealed class StubContributor(params string[] jobIds) : IBackgroundJobCatalogContributor
    {
        public IReadOnlyCollection<string> GetJobIds() => jobIds;
    }
}
