using System.Data.Common;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NGB.Hosting.AspNetCore.ErrorHandling;
using NGB.Tools.Exceptions;
using Xunit;

namespace EquipmentInspection.IntegrationTests;

public sealed class ErrorBoundaryContractTests
{
    private const string PrivateDetail = "Synthetic private provider diagnostics must stay server-side.";

    [Fact]
    public void Runtime_error_policy_preserves_control_flow_and_structured_errors_without_copying_private_messages()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = new OperationCanceledException(cancellation.Token);
        var business = new NgbArgumentInvalidException("result", "Record a result.");
        var provider = new ProbeDatabaseException();
        Assert.Same(cancelled, NgbExceptionPolicy.Apply(cancelled, "example.read"));
        Assert.Same(business, NgbExceptionPolicy.Apply(business, "example.read"));
        Assert.Same(provider, NgbExceptionPolicy.Apply(provider, "example.read"));

        var unexpected = new InvalidOperationException(PrivateDetail);
        var wrapped = Assert.IsType<NgbUnexpectedException>(NgbExceptionPolicy.Apply(unexpected, "example.read"));
        Assert.Same(unexpected, wrapped.InnerException);
        Assert.Equal(NgbErrorKind.Infrastructure, wrapped.Kind);
        Assert.Equal("ngb.unexpected", wrapped.ErrorCode);
        Assert.Equal("example.read", wrapped.Context["operation"]);
        Assert.DoesNotContain(PrivateDetail, JsonSerializer.Serialize(wrapped.Context));
        Assert.DoesNotContain(PrivateDetail, wrapped.Message);

        var timeout = new TimeoutException(PrivateDetail);
        var timedOut = Assert.IsType<NgbTimeoutException>(NgbExceptionPolicy.Apply(timeout, "example.read"));
        Assert.Same(timeout, timedOut.InnerException);
        Assert.Equal("ngb.infra.timeout", timedOut.ErrorCode);
        Assert.DoesNotContain(PrivateDetail, JsonSerializer.Serialize(timedOut.Context));
        Assert.DoesNotContain(PrivateDetail, timedOut.Message);
    }

    [Fact]
    public async Task Production_http_boundary_maps_structured_failures_and_sanitizes_unexpected_provider_messages()
    {
        var failures = new (string Name, Exception Error, HttpStatusCode Status, string Code, string Kind)[]
        {
            ("required", new NgbArgumentRequiredException("equipment_id"),
                HttpStatusCode.BadRequest, "ngb.validation.required", "Validation"),
            ("invalid", new NgbArgumentInvalidException("result", "Record a result."),
                HttpStatusCode.BadRequest, "ngb.validation.invalid_argument", "Validation"),
            ("range", new NgbArgumentOutOfRangeException("hours", 25, "At most 24 hours."),
                HttpStatusCode.BadRequest, "ngb.validation.argument_out_of_range", "Validation"),
            ("missing", new MissingInspection(),
                HttpStatusCode.NotFound, "example.inspection.missing", "NotFound"),
            ("conflict", new InspectionConflict(),
                HttpStatusCode.Conflict, "example.inspection.conflict", "Conflict"),
            ("forbidden", new InspectionForbidden(),
                HttpStatusCode.Forbidden, "example.inspection.forbidden", "Forbidden"),
            ("configuration", new NgbConfigurationViolationException(PrivateDetail),
                HttpStatusCode.InternalServerError, "ngb.configuration.violation", "Configuration"),
            ("invariant", new NgbInvariantViolationException(PrivateDetail),
                HttpStatusCode.InternalServerError, "ngb.invariant.violation", "Infrastructure"),
            ("unexpected", new InvalidOperationException(PrivateDetail),
                HttpStatusCode.InternalServerError, "ngb.unexpected", "Unknown"),
            ("wrapped", NgbExceptionPolicy.Apply(new IOException(PrivateDetail), "example.read"),
                HttpStatusCode.InternalServerError, "ngb.unexpected", "Infrastructure"),
            ("timeout", NgbExceptionPolicy.Apply(new TimeoutException(PrivateDetail), "example.read"),
                HttpStatusCode.GatewayTimeout, "ngb.infra.timeout", "Infrastructure"),
            ("provider", new ProbeDatabaseException(),
                HttpStatusCode.ServiceUnavailable, "example.provider.unavailable", "Infrastructure"),
        };
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddGlobalErrorHandling();
        builder.Services.AddSingleton<INgbExceptionHttpMapper>(new ProbeMapper());
        await using var app = builder.Build();
        app.UseExceptionHandler();
        app.MapGet("/fault/{scenario}", (string scenario) => Task.FromException(failures.Single(item => item.Name == scenario).Error));
        await app.StartAsync();
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
        using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(addresses.Addresses)) };

        foreach (var failure in failures)
        {
            using var response = await client.GetAsync("/fault/" + failure.Name);
            Assert.Equal(failure.Status, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
            var text = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(PrivateDetail, text);
            using var json = JsonDocument.Parse(text);
            var body = json.RootElement;
            Assert.Equal((int)failure.Status, body.GetProperty("status").GetInt32());
            Assert.Equal("/fault/" + failure.Name, body.GetProperty("instance").GetString());
            Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
            var error = body.GetProperty("error");
            Assert.Equal(failure.Code, error.GetProperty("code").GetString());
            Assert.Equal(failure.Kind, error.GetProperty("kind").GetString());
            if (failure.Name == "invalid")
                Assert.Equal("Record a result.", Assert.Single(error.GetProperty("errors").GetProperty("result").EnumerateArray()).GetString());
            if (failure.Name == "provider")
                Assert.Equal("probe", error.GetProperty("context").GetProperty("service").GetString());
        }
    }

    private sealed class MissingInspection()
        : NgbNotFoundException("Inspection was not found.", "example.inspection.missing");

    private sealed class InspectionConflict()
        : NgbConflictException("Reload the inspection.", "example.inspection.conflict");

    private sealed class InspectionForbidden()
        : NgbForbiddenException("Inspection access is denied.", "example.inspection.forbidden");

    private sealed class ProbeDatabaseException() : DbException(PrivateDetail);

    private sealed class ProbeMapper : INgbExceptionHttpMapper
    {
        public NgbExceptionHttpMapping? TryMap(Exception exception)
        {
            return exception is ProbeDatabaseException
                ? new NgbExceptionHttpMapping(StatusCodes.Status503ServiceUnavailable, "example.provider.unavailable",
                    NgbErrorKind.Infrastructure, new Dictionary<string, object?> { ["service"] = "probe" })
                : null;
        }
    }
}
