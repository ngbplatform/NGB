# External consumer certification

The agreed design and acceptance requirements are in [implementation-brief.md](implementation-brief.md).
Use the [platform publishing runbook](../../packaging/PUBLISHING.md) for operator steps,
external account configuration and release requirements. Application users
start with [External applications and upgrades](../../docs/architecture/external-app-upgrades.md).
The executable matrix is [matrix.json](matrix.json); it contains exact versions,
five profiles, 20 candidate gates and two post-publication gates, infrastructure
images and subsystem scope.

Run from the NGB repository root on macOS/Linux or in WSL2, with Node and local
Docker Compose available. Packaging also needs .NET 10. Certification uses the
exact versions in [toolchain.json](toolchain.json): Node 24.19.0 and .NET 10.0.401.
When a host version differs, the runner downloads official archives, verifies their
published checksums and installs private copies under `artifacts/certification-tools`.
It does not replace system tools. k6 and the complete browser matrix run in the
prepared Linux quality image on both macOS and Linux.
Profiles use ports 5180–5185 and must run sequentially on an otherwise unused host.
Every profile creates a new temporary consumer, private secrets and package extraction
directories. Download caches may be reused only within the same sealed candidate;
lockfile integrity checks and source mapping remain enabled. The clean-starter and
post-publication registry smoke always use fresh download caches too.
Disposable profile infrastructure is removed in `finally`; private diagnostics stay
in the reported temporary directory. Never upload those secrets or complete directories.

## Run a new candidate

Stop any manually started starter that occupies ports 5180–5185 before running.
Keep source and configuration unchanged from sealing until certification finishes.
The following block creates a new candidate and stops at the first failed command:

```sh
ngb_candidate="artifacts/release-candidate-local-$(date +%Y%m%d-%H%M%S)"

(
  set -e
  set -o pipefail

  npm --prefix ui ci
  npm --prefix quality ci
  node quality/upgrade-certification/verify-source.mjs
  node ui/scripts/pack-platform-ui.mjs --local-candidate
  bash packaging/nuget/pack-platform.sh
  bash packaging/nuget/verify-platform-packages.sh
  node quality/external-consumer/release.mjs seal "$ngb_candidate"
  node quality/external-consumer/release.mjs certify "$ngb_candidate" \
    2>&1 | tee "${ngb_candidate}.log"
  node quality/external-consumer/release.mjs verify-candidate "$ngb_candidate"
)
```

Success ends with both `Release certify: passed` and `Release verify-candidate: passed`.
The complete quality aggregate already runs inside certification; do not launch
`run-full-quality.sh` a second time for the same verification.

The npm archive must exist before NuGet template packing. `pack-platform.sh`
generates the template and CRM lockfiles from the candidate; review and commit
those changes before a release PR. In `platform-packages`, the NuGet job waits for
the npm job and downloads its validated archive by artifact ID before packing.

## Resume after a failure

Before creating a candidate, `seal` compares the template package's complete content
file list and SHA-256 hashes with the current template sources, including README,
hidden files and lockfiles. It applies the template project's packaging exclusions.
A mismatch reports the affected paths and stops before creating the destination;
repack with `bash packaging/nuget/pack-platform.sh`, then retry sealing.

Sealing refuses to reuse an existing destination. Changing source, matrix, frozen
fixture or package bytes invalidates it. Start a new candidate after a source repair.
For a different candidate, pass a new, nonexistent directory as the final argument to
`release.mjs seal`, then pass that same directory to `certify` and `verify-candidate`.
Do not edit an old candidate or re-seal it under the same identity. After a transient
failure during certification, keep `ngb_candidate` set to that existing directory
and run the following. In a new terminal, set it to the path printed by the failed
run first. Do not rerun the packaging/sealing block to retry certification:

```sh
(
  set -e
  : "${ngb_candidate:?Set ngb_candidate to the existing candidate directory}"

  node quality/external-consumer/release.mjs certify "$ngb_candidate"
  node quality/external-consumer/release.mjs verify-candidate "$ngb_candidate"
)
```

The runner prepares required container images and Chromium before any certification
stage. Docker pulls and tool downloads have at most three attempts with backoff;
package managers retry HTTP acquisition internally. Build, migration and test failures
are not retried automatically. The quality container runs in CI mode; Vitest and
Playwright gates reject focused tests and explicitly disable failure retries.
A `[resume]` line identifies a reused successful stage;
`[start]` and `[passed]`/`[failed]` show execution and elapsed time.

`run-state.json` records atomic checkpoints bound to the sealed manifest, exact tools,
OS/architecture, Docker engine/images, browser and relevant environment settings.
Before skipping a stage, the runner verifies its output hashes and predecessor.
Missing or changed evidence reruns that stage and everything after it. Changed input
or environment rejects resume and requires a new candidate. Failed or interrupted
stages never count as passed. Concurrent runs in one checkout are refused.
Documentation is included in source identity too. Old runs without these checkpoints
cannot be imported as successful stages.
For source-only changes outside packaged files, unchanged package bytes can be sealed
into a new candidate, but its certification starts again. Changes to platform or
template package contents require repacking first. The template's README is packaged
content too. A checkpoint is never evidence for a later source revision.
For diagnosis before sealing, the individual `certify.mjs <profile>` commands accept
`artifacts` by default. Such unsealed results do not authorize publication.

## What certification covers

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
for process launching is never manufactured. The prepared Linux image supplies
Chromium, Firefox, WebKit and their operating-system dependencies.
The release runner uses `Dockerfile.quality` and a private source copy to run the
complete aggregate on Linux. Tooling runs once, inside that aggregate. Tooling,
backend coverage, frontend coverage, the browser matrix and performance contracts
have separate checkpoints, so a later failure does not repeat earlier gates.
It never copies host `node_modules`, `bin`, `obj` or registry caches. The private Linux
workspace and its own caches are retained for the same candidate's retries. Its path
is in `quality-workspace.json`. If an unfinished aggregate must run again after
that workspace was deleted, it starts its stages from scratch.
Final evidence is written only after all profiles and aggregate stages pass; neither
a partial checkpoint nor a cached package is sufficient to authorize publication.

The Compose gate builds the generated starter, not CRM or the other existing
vertical Dockerfiles. Backend/frontend tests cover those verticals, but a passing
aggregate does not prove their Docker build contexts or packed-package imports.
Run the separate [local container checks](../../packaging/PUBLISHING.md#check-local-containers-before-publication)
when changing those paths. Performance coverage here consists of existing regression,
diagnostic-tooling and metric-contract checks; it does not run every live k6 load profile.

## Publication boundary

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

## Diagnostics and cleanup

| Observation | Meaning / action |
| --- | --- |
| `EEXIST` for the template's `.env`, followed by `[passed] template` | Expected negative test: initialization must refuse to overwrite secrets. |
| `[failed]` or a nonzero final exit status | The run has not certified the candidate; inspect the failed stage and its diagnostics. |
| A nonzero Migrator exit in the intentional Compose failure scenario | Expected only if the enclosing Compose gate then passes; it verifies that hosts stay stopped. |
| `npm audit` vulnerabilities | Separate dependency findings; passing functional gates do not mean a clean security audit. |
| `Template package does not match current sources` | Repack the template before sealing; the error lists stale, missing or extra files. No candidate directory was created. |
| `Sources differ from the sealed candidate` | Use a new candidate for the changed source; do not edit old evidence. |
| `Certification inputs or environment changed` | The saved results cannot be reused for the changed tools, Docker images/resources or settings. |

Preserve the candidate directory and Linux workspace while investigating a failure.
Diagnostics may contain private configuration; do not upload complete workspaces.
The runner removes its disposable Compose services/volumes and Linux runner container
on normal completion. After abrupt termination, retrying that candidate also cleans
up its own labelled runner container and profile resources.

Unused `ngb-template-compose-*` images are rebuildable test application images.
`ngb-certification-quality:local` is the reusable quality environment. Removing it
does not delete source, database volumes or saved reports, but rebuilding costs time;
if the resulting image ID changes, existing checkpoints cannot be resumed. Avoid
deleting images or workspaces during an active run. A fresh hosted GitHub runner
does not inherit a previous runner's local checkpoints.

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
