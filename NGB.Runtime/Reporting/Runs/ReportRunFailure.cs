using System.Text.Json;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting.Runs;

// Only errors already safe for a non-5xx response cross the queue boundary.
// Infrastructure errors and stack traces remain in server logs.
internal sealed record ReportRunFailure(
    string Code,
    string Message,
    NgbErrorKind Kind,
    IReadOnlyDictionary<string, JsonElement> Context,
    IReadOnlyDictionary<string, string[]>? Errors)
{
    internal static string? Capture(Exception exception)
    {
        if (exception is not NgbException { Kind: NgbErrorKind.Validation or NgbErrorKind.NotFound or NgbErrorKind.Conflict } error)
            return null;

        var errors = error.Context.GetValueOrDefault("errors") as IReadOnlyDictionary<string, string[]>;

        errors ??= exception switch
        {
            NgbArgumentInvalidException e => new Dictionary<string, string[]> { [e.ParamName] = [e.Reason] },
            NgbArgumentRequiredException e => new Dictionary<string, string[]> { [e.ParamName] = ["Required."] },
            NgbArgumentOutOfRangeException e => new Dictionary<string, string[]> { [e.ParamName] = [e.Reason] },
            _ => null
        };

        return JsonSerializer.Serialize(new ReportRunFailure(
            error.ErrorCode,
            error.Message,
            error.Kind,
            error.Context.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value)), errors));
    }

    internal static Exception Restore(string json)
    {
        var failure = JsonSerializer.Deserialize<ReportRunFailure>(json)!;
        var context = failure.Context
            .ToDictionary(p => p.Key, p => p.Value.ValueKind == JsonValueKind.String
                ? (object?)p.Value.GetString()
                : p.Value);

        if (failure.Errors is not null)
            context["errors"] = failure.Errors;

        return new SavedError(failure, context);
    }

    private sealed class SavedError(ReportRunFailure failure, IReadOnlyDictionary<string, object?> context)
        : NgbException(failure.Message, failure.Code, failure.Kind, context);
}
