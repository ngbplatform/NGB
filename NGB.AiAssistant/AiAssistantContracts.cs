using Microsoft.Extensions.AI;

namespace NGB.AiAssistant;

public sealed record AiAssistantMessage(string Role, string Text);

/// <summary>Untrusted page identifiers. Business data is always read through authorized MCP tools.</summary>
public sealed record AiAssistantPageContext(string Kind, string TypeCode, Guid? Id = null);

public sealed record AiAssistantRequest(
    IReadOnlyList<AiAssistantMessage> Messages,
    AiAssistantPageContext? Context = null);

public sealed record AiAssistantSource(string Kind, string TypeCode, Guid? Id, string Title)
{
    public string Reference => $"ngb-source:{Kind}:{TypeCode}" + (Id is null ? "" : $":{Id:D}");
}

/// <summary>Application data and explicit sources supplied by the trusted tool adapter.</summary>
public sealed record AiAssistantToolResult(object? Data, IReadOnlyList<AiAssistantSource> Sources);

public sealed record AiAssistantEvent(
    string Type,
    string? Text = null,
    string? Code = null,
    AiAssistantSource? Source = null);

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
