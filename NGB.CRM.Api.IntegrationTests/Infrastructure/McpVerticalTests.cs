using Microsoft.Extensions.DependencyInjection;
using NGB.CRM.Runtime;
using NGB.Testing.Mcp;
using Xunit;

namespace NGB.CRM.Api.IntegrationTests.Infrastructure;

[Collection(CrmApiProgramEnvironmentCollection.Name)]
public sealed class McpVerticalTests
{
    [Fact]
    public async Task ActualHostExposesOnlyInstalledAuthorizedReadsThroughNativeMcpClient()
    {
        var database = new CrmPostgresFixture();
        try
        {
            await database.InitializeAsync();
            using (var seedHost = CrmHostFactory.Create(database.ConnectionString,
                services => services.AddSingleton<TimeProvider>(VerticalMcpContract.Clock)))
            {
                await using var scope = seedHost.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ICrmDemoSeedService>().EnsureDemoAsync();
            }
            await VerticalMcpContract.VerifyAsync<Program>(database.ConnectionString, "crm.");
            await VerticalMcpContract.VerifyAsync<Program>(database.ConnectionString, "crm.", contentEnabled: true);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }
}
