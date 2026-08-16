using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NGB.Api.GlobalErrorHandling;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Api;

public sealed class GlobalErrorHandlingPipelineFullCoverageTests
{
    [Fact]
    public void InvalidModelStateFactory_builds_normalized_field_errors_and_default_messages()
    {
        var context = ActionContext();
        context.ModelState.AddModelError("request.name", "Required.");
        context.ModelState.AddModelError("payload.code", string.Empty);

        var result = Factory()(context).Should().BeOfType<BadRequestObjectResult>().Subject;
        result.ContentTypes.Should().Equal("application/problem+json");
        var problem = result.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Instance.Should().Be("/api/test");
        problem.Extensions["traceId"].Should().Be("trace-123");
        var error = problem.Extensions["error"].Should().BeOfType<NgbProblemError>().Subject;
        error.Code.Should().Be("ngb.validation.model_state");
        var errors = error.Errors.Should().BeAssignableTo<IReadOnlyDictionary<string, string[]>>().Subject;
        errors["name"].Should().Equal("Required.");
        errors["code"].Should().Equal("Invalid value.");
        error.Issues.Should().HaveCount(2);
    }

    [Fact]
    public void InvalidModelStateFactory_handles_an_empty_model_state_defensively()
    {
        var result = Factory()(ActionContext()).Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = result.Value.Should().BeOfType<ProblemDetails>().Subject;
        var error = problem.Extensions["error"].Should().BeOfType<NgbProblemError>().Subject;
        error.Errors.Should().BeNull();
        error.Issues.Should().BeNull();
    }

    [Theory]
    [InlineData("JSON parse failure")]
    [InlineData("LineNumber: 1")]
    [InlineData("BytePositionInLine: 2")]
    [InlineData("Path: $.name")]
    public void InvalidModelStateFactory_redacts_root_json_parser_messages(string parserMessage)
    {
        var context = ActionContext();
        context.ModelState.AddModelError("$", parserMessage);

        AssertMalformedJsonResponse(Factory()(context));
    }

    [Fact]
    public void InvalidModelStateFactory_redacts_root_json_and_bad_http_exceptions()
    {
        var json = ActionContext();
        AddException(json, "payload", new JsonException("secret parser detail"));
        AssertMalformedJsonResponse(Factory()(json));

        var badHttp = ActionContext();
        AddException(badHttp, "_form", new BadHttpRequestException("secret request detail"));
        AssertMalformedJsonResponse(Factory()(badHttp));
    }

    [Fact]
    public void InvalidModelStateFactory_preserves_field_errors_when_json_failure_is_not_root_only()
    {
        var context = ActionContext();
        context.ModelState.AddModelError("$", "JSON parse failure");
        context.ModelState.AddModelError("name", "Required.");

        var result = Factory()(context).Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = result.Value.Should().BeOfType<ProblemDetails>().Subject;
        var error = problem.Extensions["error"].Should().BeOfType<NgbProblemError>().Subject;
        error.Code.Should().Be("ngb.validation.model_state");
        error.Errors.Should().BeAssignableTo<IReadOnlyDictionary<string, string[]>>()
            .Which.Should().ContainKey("name");
    }

    [Fact]
    public async Task GlobalExceptionHandler_writes_safe_problem_json_and_uses_error_or_warning_log_levels()
    {
        var logger = new Mock<ILogger<GlobalExceptionHandler>>();
        var handler = new GlobalExceptionHandler(logger.Object);

        var serverError = HttpContext();
        (await handler.TryHandleAsync(serverError, new InvalidOperationException("secret"), default)).Should().BeTrue();
        serverError.Response.StatusCode.Should().Be(500);
        serverError.Response.ContentType.Should().StartWith("application/problem+json");
        await AssertResponse(serverError, "ngb.unexpected");

        var validation = HttpContext();
        (await handler.TryHandleAsync(validation, new NgbArgumentRequiredException("name"), default)).Should().BeTrue();
        validation.Response.StatusCode.Should().Be(400);
        await AssertResponse(validation, NgbArgumentRequiredException.Code);

        logger.Invocations.Should().Contain(invocation => invocation.Arguments.Contains(LogLevel.Error));
        logger.Invocations.Should().Contain(invocation => invocation.Arguments.Contains(LogLevel.Warning));
    }

    private static Func<ActionContext, IActionResult> Factory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGlobalErrorHandling();
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<ApiBehaviorOptions>>().Value.InvalidModelStateResponseFactory;
    }

    private static ActionContext ActionContext()
        => new(HttpContext(), new RouteData(), new ActionDescriptor(), new ModelStateDictionary());

    private static void AddException(ActionContext context, string key, Exception exception)
        => context.ModelState.AddModelError(
            key,
            exception,
            new EmptyModelMetadataProvider().GetMetadataForType(typeof(object)));

    private static DefaultHttpContext HttpContext()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "trace-123" };
        context.Request.Path = "/api/test";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static void AssertMalformedJsonResponse(IActionResult actionResult)
    {
        var result = actionResult.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = result.Value.Should().BeOfType<ProblemDetails>().Subject;
        var error = problem.Extensions["error"].Should().BeOfType<NgbProblemError>().Subject;
        error.Code.Should().Be("ngb.validation.bad_request");
        error.Context.Should().BeNull();
        error.Errors.Should().BeNull();
        error.Issues.Should().BeNull();
    }

    private static async Task AssertResponse(DefaultHttpContext context, string errorCode)
    {
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var root = document.RootElement;
        root.GetProperty("instance").GetString().Should().Be("/api/test");
        root.GetProperty("traceId").GetString().Should().Be("trace-123");
        root.GetProperty("error").GetProperty("code").GetString().Should().Be(errorCode);
    }
}
