using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using System.Text.Json;
using NGB.Tools.Exceptions;

namespace NGB.Api.GlobalErrorHandling;

internal static class ExceptionExtensions
{
    public static ProblemDetails ToProblemDetails(this Exception ex)
    {
        // Always map based on the innermost exception (common wrappers: Aggregate/TargetInvocation).
        ex = Unwrap(ex);

        // 1) Preferred path: NGB standardized exception contract.
        if (ex is INgbError ngb)
        {
            var status = MapNgbKindToStatusCode(ngb.Kind, ex);
            var builder = new ProblemDetailsBuilder(status);

            // Only include the message for non-5xx responses to reduce accidental leakage.
            if (status < StatusCodes.Status500InternalServerError)
                builder.Detail(ex.Message);

            var rawErrors = TryBuildValidationErrors(ex, ngb);
            var errors = ValidationIssueBuilder.NormalizeErrors(rawErrors);
            var issues = ValidationIssueBuilder.BuildIssues(errors);
            var extensions = BuildExtensions(ngb.ErrorCode, ngb.Kind, ngb.Context, errors, issues);
            return builder.Extensions(extensions).Build();
        }

        // 2) JSON / request format errors.
        if (ex is BadHttpRequestException or JsonException)
        {
            var errorCode = "ngb.validation.bad_request";
            var kind = NgbErrorKind.Validation;
            return new ProblemDetailsBuilder(StatusCodes.Status400BadRequest)
                .Extensions(BuildExtensions(errorCode, kind, context: null, errors: null, issues: null))
                .Build();
        }

        // 3) Infrastructure timeout.
        if (ex is TimeoutException)
        {
            var errorCode = "ngb.infra.timeout";
            var kind = NgbErrorKind.Infrastructure;
            return new ProblemDetailsBuilder(StatusCodes.Status504GatewayTimeout)
                .Extensions(BuildExtensions(errorCode, kind, context: null, errors: null, issues: null))
                .Build();
        }

        // 4) PostgreSQL common constraint errors (when they bubble up without wrapping).
        // NGB.Api already depends on a PostgreSQL health check package, so Npgsql is available.
        if (ex is Npgsql.PostgresException pg)
            return MapPostgresException(pg);

        // Status = 500 (fallback)
        return new ProblemDetailsBuilder(StatusCodes.Status500InternalServerError)
            .Extensions(BuildExtensions("ngb.unexpected", NgbErrorKind.Unknown, context: null, errors: null, issues: null))
            .Build();
    }

    private static int MapNgbKindToStatusCode(NgbErrorKind kind, Exception ex)
    {
        // Special-case: explicit timeout has a better HTTP semantics.
        if (ex is NgbTimeoutException)
            return StatusCodes.Status504GatewayTimeout;

        return kind switch
        {
            NgbErrorKind.Validation => StatusCodes.Status400BadRequest,
            NgbErrorKind.NotFound => StatusCodes.Status404NotFound,
            NgbErrorKind.Conflict => StatusCodes.Status409Conflict,
            NgbErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            // Configuration / Infrastructure are server-side faults.
            NgbErrorKind.Configuration => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status500InternalServerError
        };
    }

    private static ProblemDetails MapPostgresException(Npgsql.PostgresException pg)
    {
        // PostgreSQL SQLSTATE codes: https://www.postgresql.org/docs/current/errcodes-appendix.html
        var (status, errorCode) = pg.SqlState switch
        {
            "23505" => (StatusCodes.Status409Conflict, "ngb.conflict.unique_violation"),
            "23503" => (StatusCodes.Status409Conflict, "ngb.conflict.foreign_key_violation"),
            "40001" => (StatusCodes.Status409Conflict, "ngb.conflict.serialization_failure"),
            "40P01" => (StatusCodes.Status409Conflict, "ngb.conflict.deadlock_detected"),
            _ => (StatusCodes.Status500InternalServerError, "ngb.db.error")
        };

        var ctx = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["sqlState"] = pg.SqlState,
        };

        if (!string.IsNullOrWhiteSpace(pg.ConstraintName))
            ctx["constraint"] = pg.ConstraintName;

        if (!string.IsNullOrWhiteSpace(pg.TableName))
            ctx["table"] = pg.TableName;

        if (!string.IsNullOrWhiteSpace(pg.ColumnName))
            ctx["column"] = pg.ColumnName;

        var kind = status == StatusCodes.Status500InternalServerError
            ? NgbErrorKind.Infrastructure
            : NgbErrorKind.Conflict;

        return new ProblemDetailsBuilder(status)
            .Extensions(BuildExtensions(errorCode, kind, ctx, errors: null, issues: null))
            // Do not leak pg.MessageText; it can contain SQL fragments.
            .Build();
    }

    private static IReadOnlyDictionary<string, string[]>? TryBuildValidationErrors(Exception ex, INgbError ngb)
    {
        if (ngb.Kind != NgbErrorKind.Validation)
            return null;

        // If a domain validation exception already provided a structured errors object, forward it.
        if (ngb.Context.TryGetValue("errors", out var existing) && existing is IReadOnlyDictionary<string, string[]> dict)
            return dict;

        // Standard parameter exceptions.
        if (ex is NgbArgumentInvalidException inv)
            return new Dictionary<string, string[]>(StringComparer.Ordinal) { [inv.ParamName] = [inv.Reason] };

        if (ex is NgbArgumentRequiredException req)
            return new Dictionary<string, string[]>(StringComparer.Ordinal) { [req.ParamName] = ["Required."] };

        if (ex is NgbArgumentOutOfRangeException oor)
            return new Dictionary<string, string[]>(StringComparer.Ordinal) { [oor.ParamName] = [oor.Reason] };

        // Fallback: if we at least know the parameter name, attach the message.
        if (ngb.Context.TryGetValue("paramName", out var pn) && pn is string s && !string.IsNullOrWhiteSpace(s))
            return new Dictionary<string, string[]>(StringComparer.Ordinal) { [s] = [ex.Message] };

        return null;
    }

    private static Dictionary<string, object?> BuildExtensions(
        string errorCode,
        NgbErrorKind kind,
        IReadOnlyDictionary<string, object?>? context,
        IReadOnlyDictionary<string, string[]>? errors,
        IReadOnlyList<NgbProblemValidationIssue>? issues)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["error"] = new NgbProblemError(
                Code: errorCode,
                Kind: kind.ToString(),
                Context: context is { Count: > 0 } ? context : null,
                Errors: errors is { Count: > 0 } ? errors : null,
                Issues: issues is { Count: > 0 } ? issues : null)
        };
    }

    private static Exception Unwrap(Exception ex)
    {
        while (true)
        {
            if (ex is AggregateException agg)
            {
                var flat = agg.Flatten();
                if (flat.InnerExceptions.Count == 1)
                {
                    ex = flat.InnerExceptions[0];
                    continue;
                }
            }

            if (ex is not TargetInvocationException { InnerException: { } inner })
                return ex;

            ex = inner;
        }
    }
}
