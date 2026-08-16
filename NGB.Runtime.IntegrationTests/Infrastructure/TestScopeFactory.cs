using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// Convenience helper for integration tests:
/// - creates a Host using IntegrationHostFactory bound to an explicit fixture connection string
/// - creates a scope
/// - disposes both when scope is disposed
///
/// IMPORTANT:
/// Some NGB services (e.g. PostgresUnitOfWork) implement only IAsyncDisposable.
/// Therefore we must dispose scopes asynchronously (or block on DisposeAsync).
///
/// Usage:
/// <code>
/// using var scope = CreateScope(fixture);
/// var svc = scope.ServiceProvider.GetRequiredService&lt;...&gt;();
/// </code>
/// </summary>
internal static class TestScopeFactory
{
    public static IServiceScope CreateScope(PostgresTestFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        // IMPORTANT: use async scopes because some services implement only IAsyncDisposable.
        var scope = host.Services.CreateAsyncScope();

        return new OwnedScope(host, scope);
    }

    private sealed class OwnedScope(IHost host, AsyncServiceScope inner) : IServiceScope, IAsyncDisposable
    {
        public IServiceProvider ServiceProvider => inner.ServiceProvider;

        public void Dispose()
        {
            // xUnit tests often use `using var scope = CreateScope(fixture);`.
            // Block on async disposal to avoid DI throwing when it finds IAsyncDisposable-only services.
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();

            if (host is IAsyncDisposable asyncHost)
            {
                await asyncHost.DisposeAsync();
            }
            else
            {
                host.Dispose();
            }
        }
    }
}
