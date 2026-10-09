# External consumer certification

The agreed design and acceptance requirements are in [implementation-brief.md](implementation-brief.md).
Use the [platform publishing runbook](../../packaging/PUBLISHING.md) for operator steps,
external account configuration and release requirements. Application users
start with [External applications and upgrades](../../docs/architecture/external-app-upgrades.md).
The executable matrix is [matrix.json](matrix.json); it contains exact versions,
five profiles, 22 required gates, infrastructure images and subsystem scope.

Run from the NGB repository root on macOS/Linux or in WSL2, with .NET 10, Node
24.19.x, k6 1.2.2, Docker Compose and installed Playwright browsers.
Profiles use ports 5180–5185 and must run sequentially on an otherwise unused host.
Every run creates a new temporary consumer, private secrets and isolated caches.
Disposable profile infrastructure is removed in `finally`; private diagnostics stay
in the reported temporary directory. Never upload those secrets or complete directories.

```sh
npm --prefix ui ci
npm --prefix quality ci
cd ui
npx playwright install --with-deps chromium firefox webkit
cd ..
node quality/upgrade-certification/verify-source.mjs
node ui/scripts/pack-platform-ui.mjs --local-candidate
bash packaging/nuget/pack-platform.sh
bash packaging/nuget/verify-platform-packages.sh
node quality/external-consumer/release.mjs seal
node quality/external-consumer/release.mjs certify
node quality/external-consumer/release.mjs verify-candidate
```

The npm archive must exist before NuGet template packing. `pack-platform.sh`
generates the template and CRM lockfiles from the candidate; review and commit
those changes before a release PR. In `platform-packages`, the NuGet job waits for
the npm job and downloads its validated archive by artifact ID before packing.

Sealing refuses to reuse an existing destination. Changing source, matrix, frozen
fixture or package bytes invalidates it. Start a new candidate after any repair.
For another attempt, pass a new, nonexistent directory as the final argument to
`release.mjs seal`, then pass that same directory to `certify` and `verify-candidate`.
Do not edit an old candidate or re-seal it under the same identity. A failed
certification can leave write-once evidence, so use a fresh directory when retrying
the complete local sequence. Documentation is included in source identity too.
For diagnosis before sealing, the individual `certify.mjs <profile>` commands accept
`artifacts` by default. Such unsealed results do not authorize publication.

`verify-template.mjs` installs the packed template, checks substitutions, minimal
projects and all frontend entries/assets/classes. `verify-compose.mjs` creates the
app through the public `ngb.mjs create --packages` command and builds the
generated Dockerfiles and exercises failure ordering, private Keycloak discovery,
non-root hosts, health and browser login. The generated Dockerfiles are used unchanged;
local package acquisition is part of the public app tool. Upgrade profiles use the
public `ngb.mjs upgrade --apply` command against the frozen consumer copy.

`release.mjs certify` runs compatibility, all profiles and the complete existing
backend/frontend/performance gates, including the framework component matrix in
Chromium, Firefox and WebKit. `test-tooling.mjs` adds measured 100% coverage
for the explicit helper inventory plus negative process tests. A numeric result
for process launching is never manufactured. Run the full browser gate on a host
supporting Chromium, Firefox and WebKit; CI installs all required browser dependencies.
On macOS, the release runner uses `Dockerfile.quality` and a fresh source copy to run
the same complete quality commands on Linux. It copies only source and candidate
packages, never host `node_modules`, `bin`, `obj` or registry caches.

After successful trusted-main certification, publication automatically selects the
exact upstream run and artifact ID, waits for environment approval and uses the saved
artifacts. Configure required reviewers on `platform-release`; a missing review rule
blocks publication. Manual dispatch needs no run ID and matches the dispatch commit.
Successful publication automatically triggers `container-images`. That workflow
verifies publication/promotion evidence before building the complete image set,
and uses the manifest source commit for checkout, image tags and the deployment PR.
Failed publication or any failed image build blocks the deployment PR. Ordinary
pushes do not race package publication. Container retries use the original release
run; a manual dispatch from `main` requires evidence for that exact commit.
The old NuGet/UI workflow names are wrappers around the full unified publication,
not independently runnable package-family stages.
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
  [Build dependency advisory](#build-dependency-advisory). Their audit is not
  silently waived or reported as clean.

## Build dependency advisory

The 2026-10-07 starter audit reports the Tailwind 3 dependency chain affected by
[GHSA-vfj7-8cjw-p6xm](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm), with no patched
`braces` release. The starter pins selector parsing to 7.1.6 to address
[GHSA-rj75-hqrm-r3gf](https://github.com/advisories/GHSA-rj75-hqrm-r3gf); its production
build is exercised by certification. Existing consumers must assess their own
lockfiles. These tools run during builds; the runtime image contains
only compiled assets. Build only trusted repository content/configuration in isolated
jobs with resource limits. Do not accept user-supplied glob patterns or styles as
build configuration. The audit is not described as clean. Reassess upstream fixes
before publication; moving to Tailwind 4 is a separate architecture change.

## Maintaining the next release

Select supported exact source/target versions and update the matrix and per-package
baseline inventory. Freeze a new real source consumer from published packages;
record hashes and registry provenance before target changes. Preserve old fixtures.
Update candidate versions and the migration guide, run the complete matrix and
publish only the resulting immutable artifact set. A supported chain of adjacent
transitions does not certify an untested direct multi-minor jump.
