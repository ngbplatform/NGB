using Microsoft.Extensions.AI;
using NGB.Contracts.Reporting;

namespace NGB.AiAssistant;

public sealed record AiAssistantMessage(string Role, string Text);

/// <summary>Untrusted page identifiers. Business data is always read through authorized MCP tools.</summary>
public sealed record AiAssistantPageContext(
    string Kind,
    string TypeCode,
    Guid? Id = null,
    IReadOnlyDictionary<string, string>? Parameters = null,
    IReadOnlyDictionary<string, ReportFilterValueDto>? Filters = null);

public sealed record AiAssistantRequest(
    IReadOnlyList<AiAssistantMessage> Messages,
    AiAssistantPageContext? Context = null,
    string? TimeZone = null);

public sealed record AiAssistantSource(string Kind, string TypeCode, Guid? Id, string Title)
{
    public string Reference => $"ngb-source:{Kind}:{TypeCode}" + (Id is null ? "" : $":{Id:D}");
}

/// <summary>Application data and explicit sources supplied by the trusted tool adapter.</summary>
public sealed record AiAssistantToolResult(
    object? Data,
    IReadOnlyList<AiAssistantSource> Sources,
    bool IsError = false);

/// <summary>A value read from an authorized result, rendered as text by the UI rather than rewritten by the model.</summary>
public sealed record AiAssistantValue(string Reference, string Kind, string Value);

public sealed record AiAssistantEvent(
    string Type,
    string? Text = null,
    string? Code = null,
    AiAssistantSource? Source = null,
    AiAssistantValue? Value = null);

public sealed record AiAssistantModelInfo(string Provider, string Model);

public interface IAiAssistantToolSession : IAsyncDisposable
{
    IReadOnlyList<AIFunction> Tools { get; }
}

/// <summary>Creates a request-owned connection using the current user's identity.</summary>
public interface IAiAssistantToolSessionFactory
{
    Task<IAiAssistantToolSession> OpenAsync(CancellationToken ct);
}

public sealed class AiAssistantException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
