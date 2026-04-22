namespace NGB.Api.GlobalErrorHandling;

/// <summary>
/// Canonical machine-readable error payload carried inside <c>ProblemDetails.Extensions["error"]</c>.
/// </summary>
internal sealed record NgbProblemError(
    string Code,
    string Kind,
    object? Context,
    object? Errors,
    IReadOnlyList<NgbProblemValidationIssue>? Issues);
