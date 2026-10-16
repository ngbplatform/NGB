# NGB.Platform.Mcp

Read-only MCP integration for NGB Platform. Executes existing authorized application reads as the current user.

Use `AddNgbMcp(configuration)` and `MapNgbMcp()` in an existing NGB API host. Set `FeatureManagement:Mcp` explicitly and configure the shared `Ngb:Hosting:PublicApiUrl`. The generated application keeps MCP disabled by default.

MCP uses the existing API JWT bearer handler and the current user’s API access token. No separate MCP audience, Keycloak user or client is required to reuse that token. In Postman, choose Bearer Token. ID tokens, service accounts and inactive users are rejected. Remove obsolete `Mcp:Audience` settings.

See the [MCP guide](https://docs.ngbplatform.com/guides/mcp-server) for OAuth, client setup, limits and trusted custom contributors.
