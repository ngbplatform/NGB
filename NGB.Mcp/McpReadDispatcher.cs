using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NGB.Runtime.Security;

namespace NGB.Mcp;

/// <summary>Scoped dispatch; no principal, handler or result is retained by the immutable registry.</summary>
public sealed class McpReadDispatcher(
    McpReadToolRegistry registry,
    INgbAccessChecker access,
    IServiceProvider services,
    TimeProvider clock,
    IOptions<McpReadOptions> options)
{
    public async Task<IReadOnlyList<McpReadToolDescription>> ListAsync(CancellationToken ct)
    {
        var snapshot = await RequireActorAsync(ct);
        var result = new List<McpReadToolDescription>();

        foreach (var tool in registry.Tools)
        {
            ct.ThrowIfCancellationRequested();

            if (tool.IsPermitted(snapshot) && await tool.IsAvailableAsync(services, ct))
                result.Add(tool.Description);
        }

        return result;
    }

    public async Task<JsonElement> ExecuteAsync(string name, JsonElement input, CancellationToken ct)
    {
        var snapshot = await RequireActorAsync(ct);
        var tool = registry.Get(name);
        if (!tool.IsPermitted(snapshot) || !await tool.IsAvailableAsync(services, ct))
            throw new McpReadException("unavailable", "The requested read operation is unavailable.");

        if (input.ValueKind != JsonValueKind.Object
            || Encoding.UTF8.GetByteCount(input.GetRawText()) > options.Value.MaxInputBytes)
        {
            throw new McpReadException("invalid_input", "A bounded object request is required.");
        }

        ValidateJson(input, 0);

        // Reserve space for the second representation (JSON text). The HTTP adapter
        // also checks the complete result, including escaping and protocol fields.
        const int resultRepresentations = 2;

        return await tool.ExecuteAsync(
            services,
            input,
            clock,
            options.Value.MaxResponseBytes / resultRepresentations,
            ct);
    }

    private async Task<PermissionSnapshot> RequireActorAsync(CancellationToken ct)
    {
        var snapshot = await access.GetSnapshotAsync(ct);
        if (!snapshot.IsAuthenticated || !snapshot.IsActive)
            throw new McpReadException("access_denied", "An authenticated, active NGB user is required.");

        return snapshot;
    }

    private void ValidateJson(JsonElement value, int depth)
    {
        if (value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            depth++;

        if (depth > options.Value.MaxJsonDepth)
            throw new McpReadException("invalid_input", "The request is nested too deeply.");

        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new McpReadException("invalid_input", "Duplicate request properties are not supported.");

                if (names.Count > McpJson.MaxCollectionItems)
                    throw new McpReadException("invalid_input", "The request contains too many properties.");

                ValidateJson(property.Value, depth);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            if (value.GetArrayLength() > McpJson.MaxCollectionItems)
                throw new McpReadException("invalid_input", "The request contains too many values.");

            foreach (var item in value.EnumerateArray())
            {
                ValidateJson(item, depth);
            }
        }
        else if (value.ValueKind == JsonValueKind.String && value.GetString()!.Length > McpJson.MaxStringLength)
        {
            throw new McpReadException("invalid_input", "A request value is too long.");
        }
    }
}
