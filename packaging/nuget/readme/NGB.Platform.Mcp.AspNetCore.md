# NGB.Platform.Mcp.AspNetCore

Read-only MCP integration for NGB Platform. Executes existing authorized application reads as the current user.

Use `AddNgbMcp(configuration)` and `MapNgbMcp()` in an existing NGB API host. Set `Mcp:Enabled` explicitly and configure a canonical HTTPS resource and token audience. The generated application keeps MCP disabled by default.

See the [MCP guide](https://docs.ngbplatform.com/guides/mcp-server) for OAuth, client setup, limits and trusted custom contributors.
