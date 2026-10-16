using System.Reflection;

namespace NGB.Mcp;

internal static class McpVersion
{
    internal static string Value { get; } = typeof(McpVersion)
        .Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
        .InformationalVersion.Split('+')[0];
}
