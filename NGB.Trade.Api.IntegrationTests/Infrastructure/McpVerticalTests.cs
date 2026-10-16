using Microsoft.Extensions.DependencyInjection;
using NGB.Testing.Mcp;
using NGB.Trade.Runtime;
using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Infrastructure;

[Collection(TradeApiProgramEnvironmentCollection.Name)]
public sealed class McpVerticalTests
{
    [Fact]
    public async Task ActualHostExposesOnlyInstalledAuthorizedReadsThroughNativeMcpClient()
    {
        var database = new TradePostgresFixture();
        try
        {
            await database.InitializeAsync();
            using (var seedHost = TradeHostFactory.Create(database.ConnectionString,
                services => services.AddSingleton<TimeProvider>(VerticalMcpContract.Clock)))
            {
                await using var scope = seedHost.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ITradeDemoSeedService>().EnsureDemoAsync();
            }
            await VerticalMcpContract.VerifyAsync<Program>(database.ConnectionString, "trd.");
            await VerticalMcpContract.VerifyAsync<Program>(database.ConnectionString, "trd.", contentEnabled: true);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }
}
