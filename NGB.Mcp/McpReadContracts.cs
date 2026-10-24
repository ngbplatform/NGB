using System.Text.Json;
using NGB.Core.Security;
using NGB.Mcp.Tools;

namespace NGB.Mcp;

/// <summary>Explicit registration by a trusted, host-installed module. No assembly scanning.</summary>
public interface IMcpReadToolContributor
{
    void Contribute(IMcpReadToolRegistryBuilder builder);
}

public interface IMcpReadToolRegistryBuilder
{
    void Add<TRequest, TResult, THandler>(
        string name,
        string description,
        IReadOnlyList<NgbPermissionKey> permissions)
        where TRequest : class
        where THandler : class, IMcpReadToolHandler<TRequest, TResult>;
}

/// <summary>
/// A request-scoped read operation. Implementations must enforce object-level access and remain side-effect free.
/// The platform enforces the registration's permissions before resolving or invoking the handler.
/// </summary>
public interface IMcpReadToolHandler<in TRequest, TResult>
{
    Task<bool> IsAvailableAsync(CancellationToken ct) => Task.FromResult(true);
    Task<TResult> ExecuteAsync(TRequest request, CancellationToken ct);
}

/// <summary>Explicit sources from an authorized result. Payload fields are never interpreted as sources.</summary>
public interface IMcpReadSourceProvider<in TRequest, in TResult>
{
    ValueTask<IReadOnlyList<McpReadSource>> GetSourcesAsync(TRequest request, TResult result, CancellationToken ct);
}

public sealed record McpReadSource(McpSourceKind Kind, string TypeCode, Guid? Id, string Title, string? Url = null);

public sealed record McpReadResponse<T>(int SchemaVersion, T Data, DateTime GeneratedAtUtc)
{
    public IReadOnlyList<McpReadSource> Sources { get; init; } = [];
}

public sealed record McpReadToolDescription(
    string Name,
    string Description,
    JsonElement InputSchema,
    JsonElement OutputSchema)
{
    public bool CompatibilityOnly { get; init; }
}

public sealed record McpReadError(string Code, string Message);

public sealed class McpReadException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface IMcpNgbLinkResolver
{
    Uri? Resolve(string kind, string typeCode, Guid? id);
}
