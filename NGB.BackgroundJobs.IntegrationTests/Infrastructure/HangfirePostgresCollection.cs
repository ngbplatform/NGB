using Xunit;

namespace NGB.BackgroundJobs.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class HangfirePostgresCollection : ICollectionFixture<HangfirePostgresFixture>
{
    public const string Name = "hangfire-postgres";
}
