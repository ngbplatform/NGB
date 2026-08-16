using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NGB.Api.GlobalErrorHandling;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.Tests.Api;

public sealed class ApiErrorHandlingFullCoverageTests
{
    [Theory]
    [InlineData(StatusCodes.Status400BadRequest, "Validation failed")]
    [InlineData(StatusCodes.Status401Unauthorized, "Authorization failed")]
    [InlineData(StatusCodes.Status403Forbidden, "Access denied")]
    [InlineData(StatusCodes.Status404NotFound, "Not found")]
    [InlineData(StatusCodes.Status409Conflict, "Conflict")]
    [InlineData(StatusCodes.Status500InternalServerError, "Internal Server Error")]
    [InlineData(StatusCodes.Status503ServiceUnavailable, "Service Unavailable")]
    [InlineData(StatusCodes.Status504GatewayTimeout, "Gateway Timeout")]
    public void ProblemDetailsBuilder_builds_each_supported_template(int status, string title)
    {
        var first = new ProblemDetailsBuilder(status).Build();
        var second = new ProblemDetailsBuilder(status).Build();

        first.Status.Should().Be(status);
        first.Title.Should().Be(title);
        first.Type.Should().Be($"Status{status}{TitleSuffix(status)}");
        first.Should().NotBeSameAs(second);
    }

    [Fact]
    public void ProblemDetailsBuilder_covers_detail_extensions_and_invalid_statuses()
    {
        var extensions = new Dictionary<string, object?> { ["trace"] = 42 };
        var problem = new ProblemDetailsBuilder(StatusCodes.Status400BadRequest)
            .Detail("specific detail")
            .Extensions(extensions)
            .Build();
        problem.Detail.Should().Be("specific detail");
        problem.Extensions.Should().Contain("trace", 42);

        new ProblemDetailsBuilder(StatusCodes.Status409Conflict).Detail(" ").Extensions([]).Build().Detail
            .Should().StartWith("The request");

        Action zero = () => new ProblemDetailsBuilder(0).Build();
        Action unsupported = () => new ProblemDetailsBuilder(418).Build();
        zero.Should().Throw<NgbArgumentOutOfRangeException>();
        unsupported.Should().Throw<NgbArgumentInvalidException>();
    }

    [Theory]
    [InlineData(null, "_form")]
    [InlineData(" ", "_form")]
    [InlineData("_form", "_form")]
    [InlineData("$", "_form")]
    [InlineData("$.payload", "_form")]
    [InlineData("request.Name", "Name")]
    [InlineData("PAYLOAD.Name", "Name")]
    [InlineData("fields.Name", "Name")]
    [InlineData("$.parts.lines.rows[2].amount", "lines[2].amount")]
    [InlineData("lines.rows[3]", "lines[3]")]
    [InlineData("parts.lines.rows[4].amount", "lines[4].amount")]
    [InlineData("parts.lines.rows[]", "lines[]")]
    [InlineData(".name.", "name")]
    public void ValidationIssueBuilder_normalizes_every_supported_path(string? raw, string expected)
        => ValidationIssueBuilder.NormalizePath(raw).Should().Be(expected);

    [Theory]
    [InlineData("", "form")]
    [InlineData("_form", "form")]
    [InlineData("lines[0]", "row")]
    [InlineData("lines[]", "collection")]
    [InlineData("name", "field")]
    public void ValidationIssueBuilder_infers_scope(string path, string expected)
        => ValidationIssueBuilder.InferScope(path).Should().Be(expected);

    [Fact]
    public void ValidationIssueBuilder_normalizes_merges_deduplicates_and_ignores_blank_messages()
    {
        ValidationIssueBuilder.NormalizeErrors(null).Should().BeNull();
        ValidationIssueBuilder.NormalizeErrors(new Dictionary<string, string[]>()).Should().BeNull();
        ValidationIssueBuilder.BuildIssues(null).Should().BeNull();
        ValidationIssueBuilder.BuildIssues(new Dictionary<string, string[]>()).Should().BeNull();

        var errors = ValidationIssueBuilder.NormalizeErrors(new Dictionary<string, string[]>
        {
            ["request.name"] = ["Required.", "Required.", " "],
            ["name"] = ["Invalid."],
            [""] = ["Form error"]
        });
        errors.Should().ContainKey("name").WhoseValue.Should().Equal("Required.", "Invalid.");
        errors.Should().ContainKey("_form").WhoseValue.Should().Equal("Form error");

        ValidationIssueBuilder.BuildIssues(new Dictionary<string, string[]> { ["name"] = [" "] })
            .Should().BeNull();
        var issues = ValidationIssueBuilder.BuildIssues(errors);
        issues.Should().HaveCount(3);
        issues![0].Should().Be(new NgbProblemValidationIssue("name", "Required.", "field"));
        issues[2].Scope.Should().Be("form");
    }

    [Fact]
    public void ExceptionMapping_covers_ngb_kinds_validation_shapes_and_unwrapping()
    {
        AssertProblem(new NgbArgumentInvalidException("name", "Invalid."), 400,
            NgbArgumentInvalidException.Code, detail: "Invalid.", errorPath: "name");
        AssertProblem(new NgbArgumentRequiredException("email"), 400,
            NgbArgumentRequiredException.Code, errorPath: "email");
        AssertProblem(new NgbArgumentOutOfRangeException("age", -1, "Must be positive."), 400,
            NgbArgumentOutOfRangeException.Code, errorPath: "age");
        AssertProblem(new ValidationWithContextException(
                new Dictionary<string, object?> { ["errors"] = new Dictionary<string, string[]> { ["payload.code"] = ["Bad."] } }),
            400, "validation.context", errorPath: "code");
        AssertProblem(new ValidationWithContextException(
                new Dictionary<string, object?> { ["paramName"] = "field" }),
            400, "validation.context", errorPath: "field");
        AssertProblem(new ValidationWithContextException(
                new Dictionary<string, object?> { ["paramName"] = " " }),
            400, "validation.context");
        AssertProblem(new ValidationWithContextException(
                new Dictionary<string, object?> { ["errors"] = "not-a-dictionary" }),
            400, "validation.context");

        AssertProblem(new TestNgbException(NgbErrorKind.NotFound), 404, "test.error", detail: "test message");
        AssertProblem(new TestNgbException(NgbErrorKind.Conflict), 409, "test.error", detail: "test message");
        AssertProblem(new TestNgbException(NgbErrorKind.Forbidden), 403, "test.error", detail: "test message");
        AssertProblem(new TestNgbException(NgbErrorKind.Configuration), 500, "test.error", noDetailLeak: true);
        AssertProblem(new TestNgbException(NgbErrorKind.Infrastructure), 500, "test.error", noDetailLeak: true);
        AssertProblem(new TestNgbException(NgbErrorKind.Unknown), 500, "test.error", noDetailLeak: true);
        AssertProblem(new EmptyContextNgbException(), 500, "empty.context", noDetailLeak: true);
        AssertProblem(new NgbTimeoutException("operation", new TimeoutException("secret")), 504,
            NgbTimeoutException.Code, noDetailLeak: true);

        AssertProblem(new AggregateException(new NgbArgumentRequiredException("wrapped")), 400,
            NgbArgumentRequiredException.Code, errorPath: "wrapped");
        AssertProblem(new TargetInvocationException(new NgbArgumentRequiredException("target")), 400,
            NgbArgumentRequiredException.Code, errorPath: "target");
        AssertProblem(new AggregateException(new Exception("one"), new Exception("two")), 500,
            "ngb.unexpected", noDetailLeak: true);
    }

    [Fact]
    public void ExceptionMapping_covers_request_timeout_and_unexpected_errors()
    {
        AssertProblem(new BadHttpRequestException("secret"), 400, "ngb.validation.bad_request", noDetailLeak: true);
        AssertProblem(new JsonException("secret"), 400, "ngb.validation.bad_request", noDetailLeak: true);
        AssertProblem(new TimeoutException("secret"), 504, "ngb.infra.timeout", noDetailLeak: true);
        AssertProblem(new InvalidOperationException("secret"), 500, "ngb.unexpected", noDetailLeak: true);
    }

    [Theory]
    [InlineData("23505", 409, "ngb.conflict.unique_violation")]
    [InlineData("23503", 409, "ngb.conflict.foreign_key_violation")]
    [InlineData("40001", 409, "ngb.conflict.serialization_failure")]
    [InlineData("40P01", 409, "ngb.conflict.deadlock_detected")]
    [InlineData("53300", 503, "ngb.db.too_many_connections")]
    [InlineData("53400", 503, "ngb.db.configuration_limit_exceeded")]
    [InlineData("57P03", 503, "ngb.db.cannot_connect_now")]
    [InlineData("XX000", 500, "ngb.db.error")]
    public void ExceptionMapping_covers_postgres_states(string sqlState, int status, string code)
        => AssertProblem(new PostgresException("secret database detail", "ERROR", "ERROR", sqlState),
            status, code, noDetailLeak: true);

    [Fact]
    public void ExceptionMapping_includes_safe_postgres_identifiers_when_present()
    {
        var exception = new PostgresException(
            "secret", "ERROR", "ERROR", "23505", "detail", "hint", 1, 2,
            "query", "where", "schema", "table_name", "column_name", "type", "constraint_name",
            "file", "line", "routine");

        var error = Error(exception.ToProblemDetails());
        var context = error.Context.Should().BeAssignableTo<IReadOnlyDictionary<string, object?>>().Subject;
        context.Should().Contain("sqlState", "23505")
            .And.Contain("constraint", "constraint_name")
            .And.Contain("table", "table_name")
            .And.Contain("column", "column_name");
    }

    [Fact]
    public void ExceptionMapping_distinguishes_pool_timeouts_from_other_npgsql_failures()
    {
        AssertProblem(new NpgsqlException("outer", new TimeoutException("timeout")), 503,
            "ngb.db.connection_pool_exhausted", noDetailLeak: true);
        AssertProblem(new NpgsqlException("connection pool timeout"), 503,
            "ngb.db.connection_pool_exhausted", noDetailLeak: true);
        AssertProblem(new NpgsqlException("outer", new Exception("pool timeout")), 503,
            "ngb.db.connection_pool_exhausted", noDetailLeak: true);
        AssertProblem(new NpgsqlException("offline", new Exception("network")), 503,
            "ngb.db.unavailable", noDetailLeak: true);
    }

    private static void AssertProblem(
        Exception exception,
        int status,
        string code,
        string? detail = null,
        string? errorPath = null,
        bool noDetailLeak = false)
    {
        var problem = exception.ToProblemDetails();
        problem.Status.Should().Be(status);
        var error = Error(problem);
        error.Code.Should().Be(code);
        if (detail is not null)
            problem.Detail.Should().Be(detail);
        if (noDetailLeak)
            problem.Detail.Should().NotContain("secret");
        if (errorPath is not null)
        {
            error.Issues.Should().Contain(issue => issue.Path == errorPath);
            error.Errors.Should().BeAssignableTo<IReadOnlyDictionary<string, string[]>>()
                .Which.Should().ContainKey(errorPath);
        }
    }

    private static NgbProblemError Error(Microsoft.AspNetCore.Mvc.ProblemDetails problem)
        => problem.Extensions["error"].Should().BeOfType<NgbProblemError>().Subject;

    private static string TitleSuffix(int status) => status switch
    {
        400 => "BadRequest",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "NotFound",
        409 => "Conflict",
        500 => "InternalServerError",
        503 => "ServiceUnavailable",
        504 => "GatewayTimeout",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private sealed class TestNgbException(NgbErrorKind kind)
        : NgbException("test message", "test.error", kind,
            new Dictionary<string, object?> { ["value"] = 1 });

    private sealed class ValidationWithContextException(IReadOnlyDictionary<string, object?> context)
        : NgbValidationException("validation message", "validation.context", context);

    private sealed class EmptyContextNgbException()
        : NgbException("empty context", "empty.context", NgbErrorKind.Infrastructure);
}
