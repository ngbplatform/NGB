namespace NGB.Hosting.AspNetCore.ErrorHandling;

/// <summary>
/// Canonical validation issue contract for clients.
/// </summary>
internal sealed record NgbProblemValidationIssue(string Path, string Message, string Scope, string? Code = null);
