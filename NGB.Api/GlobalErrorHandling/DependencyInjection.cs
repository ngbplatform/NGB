using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using NGB.Tools.Exceptions;

namespace NGB.Api.GlobalErrorHandling;

public static class DependencyInjection
{
    public static IServiceCollection AddGlobalErrorHandling(this IServiceCollection services)
    {
        services
            .AddExceptionHandler<GlobalExceptionHandler>()
            .AddProblemDetails();

        // Normalize ASP.NET Core model binding / validation failures into the same envelope as NGB exceptions.
        // We keep field-level binding/validation issues as model_state, but root-level malformed JSON syntax
        // is returned as a generic bad_request envelope to avoid leaking parser details.
        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                if (IsMalformedJsonRequest(context))
                {
                    var malformedJsonProblem = BuildMalformedJsonProblemDetails(context.HttpContext);
                    return new BadRequestObjectResult(malformedJsonProblem)
                    {
                        ContentTypes = { "application/problem+json" }
                    };
                }

                var rawErrors = context.ModelState
                    .Where(kvp => kvp.Value?.Errors.Count > 0)
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value!.Errors
                            .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Invalid value." : e.ErrorMessage)
                            .ToArray(),
                        StringComparer.Ordinal);

                var errors = ValidationIssueBuilder.NormalizeErrors(rawErrors);
                var issues = ValidationIssueBuilder.BuildIssues(errors);

                var errorCode = "ngb.validation.model_state";
                var kind = nameof(NgbErrorKind.Validation);
                var error = new NgbProblemError(errorCode, kind, null, errors, issues);

                var problemDetails = new ProblemDetailsBuilder(StatusCodes.Status400BadRequest)
                    .Extensions(new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["error"] = error,
                        ["traceId"] = context.HttpContext.TraceIdentifier,
                    })
                    .Build();

                problemDetails.Instance = context.HttpContext.Request.Path;

                return new BadRequestObjectResult(problemDetails)
                {
                    ContentTypes = { "application/problem+json" }
                };
            };
        });

        return services;
    }

    private static bool IsMalformedJsonRequest(ActionContext context)
    {
        var entries = context.ModelState
            .Where(kvp => kvp.Value?.Errors.Count > 0)
            .ToArray();

        if (entries.Length == 0)
            return false;

        var hasJsonParseFailure = entries
            .SelectMany(kvp => kvp.Value!.Errors)
            .Any(error =>
                error.Exception is JsonException or BadHttpRequestException
                || error.ErrorMessage.Contains("JSON", StringComparison.OrdinalIgnoreCase)
                || error.ErrorMessage.Contains("LineNumber:", StringComparison.Ordinal)
                || error.ErrorMessage.Contains("BytePositionInLine:", StringComparison.Ordinal)
                || error.ErrorMessage.Contains("Path: $", StringComparison.Ordinal));

        if (!hasJsonParseFailure)
            return false;

        return entries.All(kvp =>
            string.Equals(
                ValidationIssueBuilder.NormalizePath(kvp.Key),
                ValidationIssueBuilder.FormPath,
                StringComparison.Ordinal));
    }

    private static ProblemDetails BuildMalformedJsonProblemDetails(HttpContext httpContext)
    {
        const string errorCode = "ngb.validation.bad_request";
        const string kind = nameof(NgbErrorKind.Validation);

        var error = new NgbProblemError(errorCode, kind, null, null, null);

        var problemDetails = new ProblemDetailsBuilder(StatusCodes.Status400BadRequest)
            .Extensions(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["error"] = error,
                ["traceId"] = httpContext.TraceIdentifier,
            })
            .Build();

        problemDetails.Instance = httpContext.Request.Path;
        return problemDetails;
    }
}
