using System.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.DependencyInjection;
using NGB.Tools.Exceptions;
using Xunit;

namespace EquipmentInspection.IntegrationTests;

public sealed class PostgresCompositionTests
{
    private const string ConnectionString = "Host=127.0.0.1;Port=1;Database=composition-only;Username=synthetic";

    [Fact]
    public async Task Provider_composition_preserves_the_application_clock_and_scopes_connections_without_opening_them()
    {
        var clock = new ApplicationClock();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddNgbPostgres(ConnectionString);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();

        Assert.Same(clock, provider.GetRequiredService<TimeProvider>());
        var unitOfWork = first.ServiceProvider.GetRequiredService<IUnitOfWork>();
        Assert.Same(unitOfWork, first.ServiceProvider.GetRequiredService<IUnitOfWork>());
        var other = second.ServiceProvider.GetRequiredService<IUnitOfWork>();
        Assert.NotSame(unitOfWork, other);
        Assert.NotSame(unitOfWork.Connection, other.Connection);
        Assert.Equal(ConnectionState.Closed, unitOfWork.Connection.State);
        Assert.Equal(ConnectionState.Closed, other.Connection.State);
        Assert.Equal("composition-only", unitOfWork.Connection.Database);
        Assert.Null(unitOfWork.Transaction);
        Assert.False(unitOfWork.HasActiveTransaction);
        await Assert.ThrowsAsync<NgbInvariantViolationException>(() => unitOfWork.CommitAsync());
        Assert.Throws<NgbInvariantViolationException>(() => unitOfWork.EnsureActiveTransaction());
        Assert.Equal(ConnectionState.Closed, unitOfWork.Connection.State);

        var options = provider.GetRequiredService<IOptions<PostgresOptions>>().Value;
        Assert.Equal(ConnectionString, options.ConnectionString);
        Assert.Equal(30, options.CommandTimeout);
        Assert.Equal(120, options.AdvisoryLockWaitTimeoutSeconds);
        Assert.Equal(5, options.OperationalRegisterPublicationTimeoutSeconds);
        Assert.False(options.EnableDetailedErrors);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4_294_967)]
    public async Task Valid_options_start_the_host_without_database_provisioning_or_migrations(int publicationTimeout)
    {
        using var host = new HostBuilder()
            .ConfigureServices(services => services.AddPostgres(options =>
            {
                options.ConnectionString = ConnectionString;
                options.CommandTimeout = 17;
                options.AdvisoryLockWaitTimeoutSeconds = 9;
                options.OperationalRegisterPublicationTimeoutSeconds = publicationTimeout;
            }))
            .Build();

        await host.StartAsync();
        try
        {
            var options = host.Services.GetRequiredService<IOptions<PostgresOptions>>().Value;
            Assert.Equal(17, options.CommandTimeout);
            Assert.Equal(9, options.AdvisoryLockWaitTimeoutSeconds);
            Assert.Equal(publicationTimeout, options.OperationalRegisterPublicationTimeoutSeconds);
            await using var scope = host.Services.CreateAsyncScope();
            Assert.Equal(ConnectionState.Closed, scope.ServiceProvider.GetRequiredService<IUnitOfWork>().Connection.State);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Theory]
    [InlineData("", 5)]
    [InlineData(" ", 5)]
    [InlineData(ConnectionString, 0)]
    [InlineData(ConnectionString, -1)]
    [InlineData(ConnectionString, 4_294_968)]
    public async Task Invalid_connection_or_publication_timeout_prevents_host_start(string connectionString, int publicationTimeout)
    {
        using var host = new HostBuilder()
            .ConfigureServices(services => services.AddPostgres(options =>
            {
                options.ConnectionString = connectionString;
                options.OperationalRegisterPublicationTimeoutSeconds = publicationTimeout;
            }))
            .Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public void Missing_registration_arguments_fail_immediately()
    {
        var services = new ServiceCollection();
        Assert.Throws<NgbArgumentRequiredException>(() => services.AddNgbPostgres(" "));
        Assert.Throws<NgbArgumentRequiredException>(() => services.AddPostgres(null!));
    }

    private sealed class ApplicationClock : TimeProvider
    {
    }
}
