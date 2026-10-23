namespace NGB.AiAssistant.AspNetCore;

public sealed class AiAssistantHttpOptions
{
    /// <summary>Initial detailed report page size. Oversized results are reduced against the actual byte budget.</summary>
    public int ReportPageSize { get; set; } = 10;

    /// <summary>Optional local Kestrel port when the public loopback port differs inside a container.</summary>
    public int? McpLoopbackPort { get; set; }

    /// <summary>Development only: trust this host's configured Kestrel certificate for loopback MCP.</summary>
    public bool TrustDevelopmentServerCertificate { get; set; }
}
