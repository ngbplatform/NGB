using FluentAssertions;
using Xunit;

namespace NGB.AgencyBilling.Api.IntegrationTests.Infrastructure;

[Collection(AgencyBillingPostgresCollection.Name)]
public sealed class AgencyBillingHostStartupValidation_P0Tests(AgencyBillingPostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Host_StartAsync_Passes_Definitions_Validation()
    {
        using var host = AgencyBillingHostFactory.Create(fixture.ConnectionString);

        var act = async () =>
        {
            await host.StartAsync();
            await host.StopAsync();
        };

        await act.Should().NotThrowAsync();
    }
}
