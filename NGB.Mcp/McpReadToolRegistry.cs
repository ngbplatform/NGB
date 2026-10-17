using System.Collections.Frozen;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NGB.Core.Security;
using NGB.Runtime.Security;

namespace NGB.Mcp;

public sealed class McpReadToolRegistry : IMcpReadToolRegistryBuilder
{
    private readonly Dictionary<string, RegisteredTool> _registrations = new(StringComparer.Ordinal);
    private readonly int _maxTools;
    private readonly bool _registeringBuiltIns;
    private readonly FrozenDictionary<string, RegisteredTool> _tools;

    public McpReadToolRegistry(IEnumerable<IMcpReadToolContributor> contributors, IOptions<McpReadOptions> options)
    {
        _maxTools = options.Value.MaxTools;
        _registeringBuiltIns = true;
        BuiltInTools.Contribute(this);
        _registeringBuiltIns = false;

        foreach (var contributor in contributors)
        {
            contributor.Contribute(this);
        }

        _tools = _registrations.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public void Add<TRequest, TResult, THandler>(
        string name,
        string description,
        IReadOnlyList<NgbPermissionKey> permissions)
        where TRequest : class
        where THandler : class, IMcpReadToolHandler<TRequest, TResult>
        => Register<TRequest, TResult, THandler>(name, description, permissions, compatibilityOnly: false);

    internal void AddCompatibility<TRequest, TResult, THandler>(
        string name,
        string description,
        IReadOnlyList<NgbPermissionKey> permissions)
        where TRequest : class
        where THandler : class, IMcpReadToolHandler<TRequest, TResult>
        => Register<TRequest, TResult, THandler>(name, description, permissions, compatibilityOnly: true);

    private void Register<TRequest, TResult, THandler>(
        string name,
        string description,
        IReadOnlyList<NgbPermissionKey> permissions,
        bool compatibilityOnly)
        where TRequest : class
        where THandler : class, IMcpReadToolHandler<TRequest, TResult>
    {
        if (_tools is not null)
            throw new InvalidOperationException("The MCP tool catalog is immutable after startup.");

        if (!Regex.IsMatch(name, "^[a-z][a-z0-9_]{2,63}$", RegexOptions.CultureInvariant)
            || string.IsNullOrWhiteSpace(description) || description.Length > 2_048)
        {
            throw new ArgumentException("MCP tools require a stable name and a bounded description.");
        }

        if (!_registeringBuiltIns
            && (name.StartsWith("ngb_", StringComparison.Ordinal)
            || name is McpToolNames.CompatibilitySearch or McpToolNames.CompatibilityFetch
            || permissions.Count == 0))
        {
            throw new ArgumentException("Custom read tools require explicit existing permissions and a non-reserved name.");
        }

        if (_registrations.Count >= _maxTools)
        {
            throw new InvalidOperationException($"Registering MCP tool '{name}' exceeds "
                + $"{nameof(McpReadOptions)}.{nameof(McpReadOptions.MaxTools)} ({_maxTools}).");
        }

        var tool = new RegisteredTool<TRequest, TResult, THandler>(name, description, permissions.ToArray(), compatibilityOnly);
        if (!_registrations.TryAdd(name, tool))
            throw new InvalidOperationException($"Duplicate MCP tool name '{name}'.");
    }

    public void ValidateHandlers(IServiceProvider services)
    {
        foreach (var tool in _tools.Values)
        {
            _ = services.GetRequiredService(tool.HandlerType);
        }
    }

    public async Task ValidatePermissionsAsync(PermissionDefinitionRegistry permissions, CancellationToken ct)
    {
        var definitions = await permissions.GetAllAsync(ct);
        var registered = definitions
            .Select(item => new NgbPermissionKey(item.ResourceKind, item.ResourceCode, item.ActionCode))
            .ToHashSet();

        foreach (var tool in _tools.Values)
        {
            if (tool.RequiredPermissions.Any(permission => !registered.Contains(permission)))
                throw new InvalidOperationException($"MCP tool '{tool.Description.Name}' requires an unregistered permission.");
        }
    }

    internal IEnumerable<RegisteredTool> Tools
        => _tools.Values.OrderBy(tool => tool.Description.Name, StringComparer.Ordinal);

    internal RegisteredTool Get(string name)
        => _tools.TryGetValue(name, out var tool)
            ? tool
            : throw new McpReadException("unsupported_tool", "The requested read tool is unavailable.");

    internal abstract class RegisteredTool(
        McpReadToolDescription description,
        IReadOnlyList<NgbPermissionKey> permissions)
    {
        internal McpReadToolDescription Description { get; } = description;
        internal IReadOnlyList<NgbPermissionKey> RequiredPermissions { get; } = permissions;
        internal abstract Type HandlerType { get; }

        internal bool IsPermitted(PermissionSnapshot snapshot) => RequiredPermissions.All(snapshot.Has);
        internal abstract Task<bool> IsAvailableAsync(IServiceProvider services, CancellationToken ct);

        internal abstract Task<JsonElement> ExecuteAsync(
            IServiceProvider services,
            JsonElement input,
            TimeProvider clock,
            int maxBytes,
            CancellationToken ct);
    }

    private sealed class RegisteredTool<TRequest, TResult, THandler>(
        string name,
        string description,
        IReadOnlyList<NgbPermissionKey> permissions,
        bool compatibilityOnly)
        : RegisteredTool(CreateDescription(name, description, compatibilityOnly), permissions)
        where TRequest : class
        where THandler : class, IMcpReadToolHandler<TRequest, TResult>
    {
        private static McpReadToolDescription CreateDescription(string name, string description, bool compatibilityOnly)
        {
            var input = McpJson.Schema<TRequest>();
            if (!McpJson.IsObjectSchema(input))
                throw new ArgumentException("MCP input schemas must describe typed request objects.");

            var output = compatibilityOnly
                ? McpJson.Schema<TResult>()
                : McpJson.Schema<McpReadResponse<TResult>>();

            return new McpReadToolDescription(name, description, input, output)
            {
                CompatibilityOnly = compatibilityOnly
            };
        }

        internal override Type HandlerType => typeof(THandler);

        internal override Task<bool> IsAvailableAsync(IServiceProvider services, CancellationToken ct)
            => services.GetRequiredService<THandler>().IsAvailableAsync(ct);

        internal override async Task<JsonElement> ExecuteAsync(
            IServiceProvider services,
            JsonElement input,
            TimeProvider clock,
            int maxBytes,
            CancellationToken ct)
        {
            TRequest request;
            try
            {
                request = input.Deserialize<TRequest>(McpJson.Options)
                    ?? throw new McpReadException("invalid_input", "An object request is required.");

                McpJson.Validate(request);
            }
            catch (JsonException exception)
            {
                var path = exception.Path is { Length: <= 256 } value ? value : "$";
                throw new McpReadException("invalid_input", $"Invalid value at {path}. Follow the input schema, including nested objects and property names.");
            }
            catch (ValidationException)
            {
                throw new McpReadException("invalid_input", "The request does not match the tool schema or limits.");
            }

            var handler = services.GetRequiredService<THandler>();
            var result = await handler.ExecuteAsync(request, ct);

            if (compatibilityOnly)
                return await McpResultWriter.SerializeAsync(result, maxBytes, ct);

            var sources = handler is IMcpReadSourceProvider<TRequest, TResult> provider
                ? await provider.GetSourcesAsync(request, result, ct)
                : [];

            var response = new McpReadResponse<TResult>(1, result, clock.GetUtcNow().UtcDateTime)
            {
                Sources = sources
            };

            return await McpResultWriter.SerializeAsync(response, maxBytes, ct);
        }
    }
}
