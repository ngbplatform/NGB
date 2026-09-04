using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NGB.Hosting.AspNetCore.ErrorHandling;
using NGB.PostgreSql.AspNetCore.DependencyInjection;
using NGB.PostgreSql.AspNetCore.ErrorHandling;
using NGB.PostgreSql.AspNetCore.Health;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.PostgreSql.AspNetCore.Tests;

public sealed class PostgresWebIntegrationAdaptersFullCoverageTests
{
    [Theory]
    [InlineData("23505", 409, "ngb.conflict.unique_violation", NgbErrorKind.Conflict)]
    [InlineData("23503", 409, "ngb.conflict.foreign_key_violation", NgbErrorKind.Conflict)]
    [InlineData("40001", 409, "ngb.conflict.serialization_failure", NgbErrorKind.Conflict)]
    [InlineData("40P01", 409, "ngb.conflict.deadlock_detected", NgbErrorKind.Conflict)]
    [InlineData("53300", 503, "ngb.db.too_many_connections", NgbErrorKind.Infrastructure)]
    [InlineData("53400", 503, "ngb.db.configuration_limit_exceeded", NgbErrorKind.Infrastructure)]
    [InlineData("57P03", 503, "ngb.db.cannot_connect_now", NgbErrorKind.Infrastructure)]
    [InlineData("XX000", 500, "ngb.db.error", NgbErrorKind.Infrastructure)]
    public void Exception_mapper_covers_postgres_states(
        string sqlState,
        int statusCode,
        string errorCode,
        NgbErrorKind kind)
    {
        var mapping = new PostgresExceptionHttpMapper().TryMap(
            new PostgresException("secret database detail", "ERROR", "ERROR", sqlState));

        mapping.Should().NotBeNull();
        mapping!.StatusCode.Should().Be(statusCode);
        mapping.ErrorCode.Should().Be(errorCode);
        mapping.Kind.Should().Be(kind);
        mapping.Context.Should().Contain("sqlState", sqlState);
    }

    [Fact]
    public void Exception_mapper_includes_only_safe_postgres_identifiers()
    {
        var exception = new PostgresException(
            "secret", "ERROR", "ERROR", "23505", "detail", "hint", 1, 2,
            "query", "where", "schema", "table_name", "column_name", "type", "constraint_name",
            "file", "line", "routine");

        var context = new PostgresExceptionHttpMapper().TryMap(exception)!.Context!;
        context.Should().Contain("sqlState", "23505")
            .And.Contain("constraint", "constraint_name")
            .And.Contain("table", "table_name")
            .And.Contain("column", "column_name");
        context.Values.Should().NotContain("secret");
    }

    [Fact]
    public void Exception_mapper_handles_client_failures_and_ignores_other_exceptions()
    {
        var mapper = new PostgresExceptionHttpMapper();

        mapper.TryMap(new NpgsqlException("outer", new TimeoutException("timeout")))!.ErrorCode
            .Should().Be("ngb.db.connection_pool_exhausted");
        mapper.TryMap(new NpgsqlException("connection pool timeout"))!.ErrorCode
            .Should().Be("ngb.db.connection_pool_exhausted");
        mapper.TryMap(new NpgsqlException("outer", new Exception("pool timeout")))!.ErrorCode
            .Should().Be("ngb.db.connection_pool_exhausted");
        mapper.TryMap(new NpgsqlException("offline", new Exception("network")))!.ErrorCode
            .Should().Be("ngb.db.unavailable");
        mapper.TryMap(new InvalidOperationException()).Should().BeNull();
        FluentActions.Invoking(() => mapper.TryMap(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Registration_validates_inputs_and_registers_trimmed_health_name()
    {
        Action nullServices = () => PostgresAspNetCoreServiceCollectionExtensions
            .AddNgbPostgresExceptionMapping(null!);
        Action nullBuilder = () => PostgresAspNetCoreServiceCollectionExtensions
            .AddNgbPostgresHealthCheck(null!, "Host=localhost");
        Action blankConnection = () => new ServiceCollection().AddHealthChecks()
            .AddNgbPostgresHealthCheck(" ");
        Action blankName = () => new ServiceCollection().AddHealthChecks()
            .AddNgbPostgresHealthCheck("Host=localhost", " ");

        nullServices.Should().Throw<ArgumentNullException>();
        nullBuilder.Should().Throw<ArgumentNullException>();
        blankConnection.Should().Throw<NgbArgumentRequiredException>();
        blankName.Should().Throw<NgbArgumentRequiredException>();

        var services = new ServiceCollection();
        services.AddNgbPostgresExceptionMapping();
        services.AddHealthChecks().AddNgbPostgresHealthCheck(" Host=localhost ", " Database ");
        using var provider = services.BuildServiceProvider();
        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Should().ContainSingle().Subject;

        registration.Name.Should().Be("Database");
        registration.Factory(provider).Should().BeOfType<PostgresHealthCheck>();
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(INgbExceptionHttpMapper)
            && descriptor.ImplementationType == typeof(PostgresExceptionHttpMapper));
    }

    [Fact]
    public async Task Health_check_reports_success_failure_and_preserves_cancellation()
    {
        var context = new HealthCheckContext();
        var healthy = new PostgresHealthCheck(_ => Task.CompletedTask);
        var unhealthy = new PostgresHealthCheck(_ => throw new NpgsqlException("offline"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = new PostgresHealthCheck(ct => Task.FromCanceled(ct));

        (await healthy.CheckHealthAsync(context)).Status.Should().Be(HealthStatus.Healthy);
        var failure = await unhealthy.CheckHealthAsync(context);
        failure.Status.Should().Be(HealthStatus.Unhealthy);
        failure.Exception.Should().BeOfType<NpgsqlException>();
        await FluentActions.Invoking(() => cancelled.CheckHealthAsync(context, cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Exception_mapper_registration_is_idempotent()
    {
        var services = new ServiceCollection();

        services.AddNgbPostgresExceptionMapping().AddNgbPostgresExceptionMapping();

        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(INgbExceptionHttpMapper)
            && descriptor.ImplementationType == typeof(PostgresExceptionHttpMapper));
    }
}
