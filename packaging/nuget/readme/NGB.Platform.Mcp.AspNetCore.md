# NGB.Platform.Mcp.AspNetCore

Read-only MCP integration for NGB Platform. Executes existing authorized application reads as the current user.

Use `AddNgbMcp(configuration)` and `MapNgbMcp()` in an existing NGB API host. Set `FeatureManagement:Mcp` explicitly and configure the shared `Ngb:Hosting:PublicApiUrl`. MCP appends its mapped route (`/mcp` by default) and uses that URI as the default token audience. `Mcp:Resource` is an optional full endpoint override. The generated application keeps MCP disabled by default.

See the [MCP guide](https://docs.ngbplatform.com/guides/mcp-server) for OAuth, client setup, limits and trusted custom contributors.
