using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NGB.Persistence.Migrations;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.DependencyInjection;
using NGB.PostgreSql.UnitOfWork;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests;

public sealed class PostgresDependencyInjectionFullCoverageTests
{
    [Fact]
    public async Task Registration_validates_inputs_configures_options_and_materializes_factories()
    {
        Action blank = () => new ServiceCollection().AddNgbPostgres(" ");
        Action missingConfigure = () => new ServiceCollection().AddPostgres(null!);
        blank.Should().Throw<NgbArgumentRequiredException>();
        missingConfigure.Should().Throw<NgbArgumentRequiredException>();

        const string connectionString = "Host=localhost;Database=ngb_unit;Username=unit;Password=unit";
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<PostgresUnitOfWork>>(NullLogger<PostgresUnitOfWork>.Instance);
        services.AddNgbPostgres(connectionString);
        await using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<PostgresOptions>>().Value;
        options.ConnectionString.Should().Be(connectionString);
        options.CommandTimeout.Should().Be(30);
        options.AdvisoryLockWaitTimeoutSeconds.Should().Be(120);
        options.EnableDetailedErrors.Should().BeFalse();
        options.CommandTimeout = 45;
        options.AdvisoryLockWaitTimeoutSeconds = 60;
        options.EnableDetailedErrors = true;
        options.Should().BeEquivalentTo(new
        {
            ConnectionString = connectionString,
            CommandTimeout = 45,
            AdvisoryLockWaitTimeoutSeconds = 60,
            EnableDetailedErrors = true
        });

        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);
        provider.GetRequiredService<IMigrationRunner>().Should().NotBeNull();
        await using var scope = provider.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        uow.Should().BeOfType<PostgresUnitOfWork>();
    }

    [Fact]
    public void Empty_configured_connection_string_fails_options_validation()
    {
        var services = new ServiceCollection();
        services.AddPostgres(_ => { });
        using var provider = services.BuildServiceProvider();

        Action resolve = () => _ = provider.GetRequiredService<IOptions<PostgresOptions>>().Value;
        resolve.Should().Throw<OptionsValidationException>();
    }
}
