# External consumer certification

The normative release plan is [implementation-brief.md](implementation-brief.md).
The executable matrix is [matrix.json](matrix.json); it contains exact versions,
five profiles, 22 required gates, infrastructure images and subsystem scope.

Run with .NET 10, Node 24.19.x, k6 1.2.2, Docker Compose and installed Playwright browsers.
Profiles use ports 5180–5185 and must run sequentially on an otherwise unused host.
Every run creates a new temporary consumer, private secrets and isolated caches.
Disposable profile infrastructure is removed in `finally`; private diagnostics stay
in the reported temporary directory. Never upload those secrets or complete directories.

```sh
npm --prefix ui ci
npm --prefix quality ci
node quality/upgrade-certification/verify-source.mjs
node ui/scripts/pack-platform-ui.mjs --local-candidate
bash packaging/nuget/pack-platform.sh
bash packaging/nuget/verify-platform-packages.sh
node quality/external-consumer/release.mjs seal
node quality/external-consumer/release.mjs certify
node quality/external-consumer/release.mjs verify-candidate
```

Sealing refuses to reuse an existing destination. Changing source, matrix, frozen
fixture or package bytes invalidates it. Start a new candidate after any repair.
For diagnosis before sealing, the individual `certify.mjs <profile>` commands accept
`artifacts` by default. Such unsealed results do not authorize publication.

`verify-template.mjs` installs the packed template, checks substitutions, minimal
projects and all frontend entries/assets/classes. `verify-compose.mjs` builds the
generated Dockerfiles and exercises failure ordering, private Keycloak discovery,
non-root hosts, health and browser login. Its only consumer changes are candidate
package acquisition (isolated NuGet feed and exact npm archive cache).

`release.mjs certify` runs compatibility, all profiles and the complete existing
backend/frontend/performance gates, including the framework component matrix in
Chromium, Firefox and WebKit. `test-tooling.mjs` adds measured 100% coverage
for the explicit helper inventory plus negative process tests. A numeric result
for process launching is never manufactured. Run the full browser gate on a host
supporting Chromium, Firefox and WebKit; CI installs all required browser dependencies.
On macOS, the release runner uses `Dockerfile.quality` and a fresh source copy to run
the same complete quality commands on Linux. It copies only source and candidate
packages, never host `node_modules`, `bin`, `obj` or registry caches.

After a successful trusted-main certification, publication uses the saved artifacts.
`verify-registry.mjs` is a post-publication gate, so it cannot pass for an unpublished
candidate. It never reuses the candidate feed for its registry-only consumer.
`release.mjs verify-promotion` requires the registry receipt in addition to the
complete candidate evidence. See the [upgrade guide](../../docs/architecture/external-app-upgrades.md).

## Audit findings that drive this release

- Existing packages already contained most reusable API, worker, migration and UI
  functions. External consumption did not require a new frontend architecture.
- There was no official independent application template or common five-profile
  upgrade certification pipeline. Existing vertical regression is retained.
- The frontend shared Tailwind configuration was repository-owned. The public
  preset now supplies that contract; the public Vite plugin also has a declaration.
- Container authentication needed private discovery with a separately validated
  public issuer. `MetadataAddress` is additive and keeps issuer validation intact.
- A fresh direct administrator route could run its guard before access loaded.
  The starter resolves access before mounting the router.
- Runtime baselines needed explicit first-publication versions for the three
  content packages. The content-only template is classified separately.
- Previous publication workflows rebuilt packages independently. The unified
  pipeline now publishes only the certified bytes and verifies registry identity.
- Tailwind 3 build dependencies have outstanding upstream advisories, recorded in
  the upgrade guide. Their audit is not silently waived or reported as clean.

## Maintaining the next release

Select supported exact source/target versions and update the matrix and per-package
baseline inventory. Freeze a new real source consumer from published packages;
record hashes and registry provenance before target changes. Preserve old fixtures.
Update candidate versions and the migration guide, run the complete matrix and
publish only the resulting immutable artifact set. A supported chain of adjacent
transitions does not certify an untested direct multi-minor jump.
