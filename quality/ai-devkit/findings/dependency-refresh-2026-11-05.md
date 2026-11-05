# Dependency refresh for the 3.4.0 Kit and template

Status: verified on 2026-11-05. Platform and vertical dependency versions selected
by the user are preserved. Shared direct dependencies in the current Kit example,
starter and active acceptance projects now match those selections. Historical
upgrade fixtures, archived evaluations and the platform release version remain
unchanged.

## NU1608 cause and correction

The source `NGB.Mcp.AspNetCore.csproj` already referenced
`ModelContextProtocol.AspNetCore` 2.3.1, but the local
`NGB.Platform.Mcp.AspNetCore` 3.4.0 archive and NuGet cache still declared 2.2.0.
Vertical integration tests directly selected `ModelContextProtocol` 2.3.1,
conflicting with the transport package's exact 2.2.0 requirement.

The standard packaging script rebuilt 32 platform/template packages and 31 symbol
packages, refreshed both local feeds, invalidated their replaced 3.4.0 cache
entries and restored solution dependencies. All 13 restored project graphs using
MCP now resolve `ModelContextProtocol`, `.AspNetCore` and `.Core` to 2.3.1.
No NU1608 diagnostic remains in restored solution assets. No warning suppression
or artificial package override was added to CRM.

## Kit and template synchronization

- Shared Microsoft.AspNetCore and Microsoft.Extensions references: 10.0.14.
- Microsoft.NET.Test.Sdk: 18.11.0; xunit.runner.visualstudio: 4.0.1.
- Testcontainers and Testcontainers.PostgreSql: 4.16.0.
- The external MCP acceptance client: ModelContextProtocol 2.3.1.
- Testcontainers.Minio retains the platform's existing 4.15.0 selection.

Template lockfiles were regenerated from the rebuilt packages. Package-derived
contract snapshots, Kit ownership hashes and vendor copies were regenerated.
The type/method-name/field-name inventory is unchanged; package and assembly
identities now reference the rebuilt artifacts. The final template and CLI were
repacked after generation, and both local NuGet feeds contain the same archives.

## Verification

Evidence: `artifacts/ai-devkit/dependency-refresh-2026-11-05/summary.json`.

- CRM API integration-test project: Release build passed with **0 warnings and
  0 errors**, with NU1608 promoted to an error.
- Native MCP SDK HTTP discovery/invocation: **2 tests passed**, covering both
  supported protocol versions.
- Fresh template archive in an isolated directory/cache: locked restore and
  complete backend build passed; **13 unit tests passed**.
- Independent Kit example in an isolated directory/cache: restore and complete
  backend build passed; **102 unit tests and 3 MCP HTTP tests passed**. HTTP tests
  use disposable PostgreSQL/Keycloak infrastructure and verify both administrator
  identities, permissions, journal metadata/detail and compatibility fetch.
- Kit guidance/capability/ownership checks: **16 tests passed**.
- Canonical/generated Kit equality, CLI payload equality, template archive
  equality and local-feed equality passed.

These are targeted dependency checks. The full Backend/Frontend suites were not
rerun, and these results do not represent a new full release certificate.
