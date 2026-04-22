using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NGB.Tools.Exceptions;

namespace NGB.Api.GlobalErrorHandling;

internal sealed class ProblemDetailsBuilder(int statusCode)
{
    private string? _detail;
    private Dictionary<string, object?> _extensions = new(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<int, Func<ProblemDetails>> Templates =
        new Dictionary<int, Func<ProblemDetails>>
    {
        [StatusCodes.Status400BadRequest] = () => new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Type = nameof(StatusCodes.Status400BadRequest),
            Title = "Validation failed",
            Detail = "One or more validation errors has occurred.",
        },

        [StatusCodes.Status401Unauthorized] = () => new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Type = nameof(StatusCodes.Status401Unauthorized),
            Title = "Authorization failed",
        },

        [StatusCodes.Status403Forbidden] = () => new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Type = nameof(StatusCodes.Status403Forbidden),
            Title = "Access denied",
        },

        [StatusCodes.Status404NotFound] = () => new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Type = nameof(StatusCodes.Status404NotFound),
            Title = "Not found",
        },

        [StatusCodes.Status409Conflict] = () => new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Type = nameof(StatusCodes.Status409Conflict),
            Title = "Conflict",
            Detail = "The request could not be completed due to a conflict with the current state.",
        },

        [StatusCodes.Status500InternalServerError] = () => new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Type = nameof(StatusCodes.Status500InternalServerError),
            Title = "Internal Server Error",
            Detail = "Something went wrong.",
        },

        [StatusCodes.Status504GatewayTimeout] = () => new ProblemDetails
        {
            Status = StatusCodes.Status504GatewayTimeout,
            Type = nameof(StatusCodes.Status504GatewayTimeout),
            Title = "Gateway Timeout",
            Detail = "The operation timed out.",
        },
    };

    public ProblemDetailsBuilder Detail(string detail)
    {
        _detail = detail;
        return this;
    }
    
    public ProblemDetailsBuilder Extensions(Dictionary<string, object?> extensions)
    {
        _extensions = extensions;
        return this;
    }
    
    public ProblemDetails Build()
    {
        if (statusCode <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(statusCode), statusCode, "Status code must be a positive integer.");
        
        if (!Templates.TryGetValue(statusCode, out var templateFactory))
            throw new NgbArgumentInvalidException(nameof(statusCode), $"Status code '{statusCode}' is not supported by problem details.");

        // Always create a NEW instance per response (ProblemDetails is mutable).
        var result = templateFactory();

        if (!string.IsNullOrWhiteSpace(_detail))
            result.Detail = _detail;
        
        if (_extensions.Count > 0)
        {
            foreach (var (k, v) in _extensions)
            {
                result.Extensions[k] = v;
            }
        }

        return result;
    }
}
