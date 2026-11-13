# Publishing NGB Platform Packages

NGB publishes `NGB.Platform.*` on nuget.org and `@ngbplatform/ui` plus
`@ngbplatform/cli` on npmjs.com.
The template is included in the NuGet family. PM, Trade and Agency Billing build
from platform source; CRM release images restore platform packages from registries.

This maintainer runbook covers release approval, trusted publishing, container
deployment and retries.
The normal configured sequence is:

**Local tests + full coverage + FULL Certification → PR → package build → merge →
GitHub package build on main → approval → publication and registry identity
verification → container builds → infrastructure deployment PR.**

Backend/Frontend suites, full coverage, FULL Certification and Codex/Claude checks
run locally before release. GitHub does not repeat them or import their reports.
It builds NuGet/npm packages from the release commit; no local packages or reports
need to be uploaded. Local certification is the maintainer's release prerequisite,
not an automatically verified GitHub gate.

## Configure release access once

These settings authorize the unified publication workflow. A previous successful
release through separate NuGet/npm workflows does not establish this configuration.

### GitHub approval

1. Open [NGB repository environments](https://github.com/ngbplatform/NGB/settings/environments).
2. Open `platform-release`, or use **New environment**, enter `platform-release`
   and click **Configure environment**.
3. Enable **Required reviewers**, select your reviewer and **Save protection rules**.
   Leave **Prevent self-review** disabled if you must approve runs you triggered.
4. Under **Deployment branches and tags**, choose **Selected branches and tags**,
   then **Add deployment branch or tag rule** → **Branch** → `main` → **Add rule**.

The selection job rejects a missing or empty required-reviewer rule. On GitHub
Free/Pro/Team, required reviewers are available only for public repositories.
See [GitHub environment configuration](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments).

### NuGet trusted publishing

Sign in to nuget.org → username menu → **Trusted Publishing** → add a policy:

| Field | Value |
| --- | --- |
| Repository Owner | `ngbplatform` |
| Repository | `NGB` |
| Workflow File | `publish-platform-release.yml` |
| Environment | `platform-release` |
| Policy owner | The NuGet account or organization owning the NGB packages |
| Package glob | `NGB.Platform.*` |
| Scopes | New packages and new versions |

Allowing new packages is necessary for the first `NGB.Platform.Templates` release.
The workflow defaults to the NuGet username `ngb_platform`. If your login differs,
set the repository **Settings → Secrets and variables → Actions → Variables** entry
`NUGET_USER` to that NuGet username, not an email address. The username must identify
the account with the policy. Save the policy; if NuGet shows a temporary activation
window, publish within that window or reactivate it before release.
See [NuGet trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing).

### npm trusted publishing

For both `@ngbplatform/ui` and `@ngbplatform/cli`, configure **Settings** →
**Trusted Publisher** → **GitHub Actions**. Use **Add trusted publisher** if another
connection already exists:

| Field | Value |
| --- | --- |
| Organization or user | `ngbplatform` |
| Repository | `NGB` |
| Workflow filename | `publish-platform-release.yml` |
| Environment name | `platform-release` |
| Allowed actions | Allow `npm publish` |

Use the filename only, without `.github/workflows/`. This is different from the
Actions display name `publish-platform-release`. Stage-only permission
does not authorize this workflow's direct publish command. Newly created npm
connections require their first successful publication within two days; recreate an
expired connection. See [npm trusted publishers](https://docs.npmjs.com/trusted-publishers/).

NuGet/npm publication uses OIDC; permanent registry API-token secrets are not required.
Use the unified workflow for releases. The old `publish-platform-nuget` and
`publish-platform-ui` entry points both call it and publish both package families.
They are not separate publication stages. Invoking a wrapper also requires trust
for that calling workflow; npm validates the caller of a reusable workflow.

### Infrastructure deployment PR

In the NGB repository, **Settings → Secrets and variables → Actions → Secrets**
must contain `NGB_PLATFORM_INFRA_PR_TOKEN`. It must permit checkout, branch push
and PR creation in `ngbplatform/ngb-platform-infra`. This is separate from registry
OIDC. Without it, images can build successfully but `propose infra deploy` fails.

The workflow updates `apps/{pm,trade,ab,crm}/envs/prod/values.yaml` in that repository.
Its merge/deployment rules and Argo CD configuration must already be set up there.

## Release step by step

Before opening the PR, choose an unpublished target version and align the platform,
UI, template, CLI, Kit and CRM dependencies, the certification matrix, package baselines and
registered CLI transition. Record changes and migration instructions. A repeated
publication of an existing version is only a retry of the identical published set.
Prepare local candidate packages using [Prepare local artifacts](#prepare-local-artifacts),
then review and commit the generated template and CRM lockfiles. The npm package
job and CRM image build consume the committed CRM lockfile; container jobs do not
replace it with the copy saved in certification evidence. A changed UI archive
therefore requires regenerating and committing its matching lockfile before merge.

| Step | What you do | What to check |
| --- | --- | --- |
| 1 | Commit and push the feature branch. In GitHub, open a PR targeting `main`. | The PR includes the intended version, migration documentation and source changes. |
| 2 | Open the PR's **Checks** tab and wait. | `platform-packages` builds the packages when its path filters trigger. Complete all mandatory local checks before releasing. |
| 3 | Review and merge the PR in GitHub. | Do not run the old publish workflows from the feature branch. |
| 4 | Open **Actions → platform-packages** and select the new `main` run. | It builds the merged commit and uploads `ngb-platform-release-attempt-<run_attempt>`. A PR build cannot authorize publication. |
| 5 | Open **Actions → publish-platform-release**, select the matching run and inspect its summary. | Commit, package build, attempt and artifact identify the release you intend to publish. |
| 6 | Click **Review deployments**, select `platform-release`, then **Approve and deploy**. | The publication job succeeds, including registry verification and promotion evidence. |
| 7 | Open **Actions → container-images** and wait for the automatically triggered run. | Release evidence, CRM registry checks, all 20 image builds and `propose infra deploy` succeed. |
| 8 | Open the generated PR in `ngbplatform/ngb-platform-infra`. Review its image tags and merge it. | All four workloads reference `sha-<released-commit>`. Check the deployment through your configured Argo CD process. |

No run IDs or artifact IDs are entered manually. GitHub starts this flow from the
main package build; the maintainer completes the local checks before approval.
Approval authorizes package publication; the infrastructure PR is a separate
deployment review. Registry publication alone does
not mean an application has been deployed.

The workflows do not create a Git tag or GitHub Release entry. When recording a
GitHub Release, create/select the tag at the verified release commit and publish
the matching changelog after package verification succeeds. Do not tag a newer
`main` commit just because it is now the branch tip.

## What each workflow does

| Actions name | Purpose |
| --- | --- |
| `platform-packages` | Build UI, NuGet, template and CLI packages; check package contents and record commit, build attempt and archive identities. |
| `publish-platform-release` | Select the successful main build for the exact commit, wait for approval, publish its saved archives and verify registry identities/signatures. |
| `container-images` | Verify publication evidence, build the released commit and propose the infrastructure update. |

In `platform-packages`, the NuGet job waits for the npm build and downloads that
job's archive by artifact ID into `artifacts/npm`. Template lockfile generation
consumes this archive. NuGet packing also prepares the Kit and packs the CLI.
The release bundle includes all NuGet/symbol packages, the UI and CLI archives,
and a `github-package-build` manifest. It contains no local certification reports.
The publisher consumes that exact build attempt without rebuilding packages.
The 20 vertical images are built by `container-images` after publication.

`external-app-certification` and `performance-tests` no longer run in GitHub.
If repository branch protection/rulesets require either deleted check, remove
those check requirements in repository settings; workflow files cannot update
server-side rulesets. Keep the package build check if it is required by policy.

Container cache export is optional: both image matrices use `ignore-error=true`
only for `cache-to`. A GitHub cache write failure does not invalidate a successful
image build and GHCR push. Build/push errors still fail the job and block the
deployment PR; the workflow does not use `continue-on-error` for those steps.

The GitHub release package artifact is retained for 30 days and publication evidence for 90 days
under the checked-in workflow settings. Downloads select the exact artifact ID,
not an arbitrary artifact with the same name.
Publication evidence names include the workflow attempt number. Container selection
uses the successful attempt, retaining earlier failed-attempt archives for diagnostics.
For older runs with the shared `ngb-publication-evidence` name, selection matches the
archive creation time to that attempt's successful evidence upload step. Missing,
expired or ambiguous evidence blocks image builds; it never falls back to an older attempt.

The build manifest records source commit/tree, build run/attempt and archive hashes.
Generated template locks are inside the template package. CRM images use committed
lockfiles; generate and review those before the PR rather than editing integrity values. Registry verification checks NuGet content/signatures and npm
integrity. Source, image tags and deployment PR all identify the released commit.
Registry downloads share a 20-minute deadline for the entire NuGet/npm package set.
Available packages proceed immediately; temporary HTTP/connection failures retry
every 15 seconds, or later when the registry supplies `Retry-After`, within that deadline.
Logs show the affected URL, retry number and remaining time. Access errors, package
identity/signature mismatches stop verification without retries. GitHub does not
start a registry-only application or run browser/runtime tests after publication.

An ordinary push does not publish images. A vertical-only change needs publication
evidence for its exact commit to enter this release train; independent vertical
release pipelines are outside this workflow's scope.

## Retry a failed stage

| Failure | Action |
| --- | --- |
| PR or main package build failed | Inspect the failed build/packaging step. Fix it in a PR or rerun a transient failure for the same commit. Nothing has been published yet. Local tests/certification remain separate. |
| Release selection reports no reviewer or an OIDC policy mismatch | Correct the external settings, then rerun the failed publication job. Check workflow filename and environment first. |
| Upload or registry verification failed transiently | Rerun the original publication run, retaining the same GitHub build artifact. Existing npm integrity must match; NuGet duplicates are checked during registry verification. |
| An older container run reports multiple `ngb-publication-evidence` artifacts | That run still uses the old selector. Identify the archive IDs in each publication attempt's `Record publication and promotion evidence` log. Preserve the successful attempt's archive; download any failed-attempt archives needed for diagnostics before deleting only those failed-attempt archives. Then rerun the failed container jobs. Do not repack or republish the released version. |
| Published contents are wrong or differ from the selected build package | Prepare a corrected new version. Published versions cannot be overwritten. |
| Container build or infrastructure PR creation failed | For transient registry, runner or credential failures, repair the environment and rerun that release's container run. It uses the same verified commit. A source/Dockerfile fix needs a new commit and certification; rerunning the old run cannot consume the fix. |
| GitHub build artifact expired | Rerun the package build for the release commit. Do not replace a partially published release with different bytes of the same version; use a new version if identity differs. |

For manual dispatch, select **Run workflow → main**. Publication requires successful
main package build for that exact dispatch commit; container dispatch requires
successful publication evidence for it. When `main` has advanced, use the original
run's **Re-run jobs** controls for the older release instead of starting a new run
at the branch tip.

## Prepare local artifacts

From the repository root on macOS/Linux or in WSL2, with .NET 10 and Node 24.19.x:

```sh
npm --prefix ui ci
npm --prefix quality ci
node ui/scripts/pack-platform-ui.mjs --local-candidate
bash packaging/nuget/pack-platform.sh
bash packaging/nuget/verify-platform-packages.sh
node quality/external-consumer/release.mjs seal
```

The order matters: template packing consumes the already packed npm archive.
`pack-platform.sh` selects the Node/.NET versions in
`quality/external-consumer/toolchain.json` and uses the Git commit timestamp for
deterministic NuGet archives. After building runtime packages it checks their
public contracts, refreshes package and assembly identities, renders the Kit,
generates consumer locks, then packs the template and CLI. Changes to public
contracts require capability review before explicit reference regeneration.
The generated locks contain template NuGet hashes, template npm integrity and CRM
npm integrity. Review and commit the derived lockfile changes
before opening the release PR: CRM image jobs consume the committed lockfile.

`--local-candidate` permits local packing of the UI before the derived lockfiles
are generated; it does not waive later artifact or registry identity checks.
GitHub uses the strict UI packer without this flag: the committed CRM lockfile must
match the packed archive exactly. Do not hand-edit its
integrity or introduce a second manual npm installation procedure.

Artifacts are ignored by Git. The default sealed directory is
`artifacts/release-candidate`; it must not exist before `seal`. For a new candidate,
use a new path, for example `release.mjs seal artifacts/release-candidate-next`,
and pass that path to subsequent commands. Source changes after sealing, including
documentation changes, invalidate source identity for certification.
`seal` checks the package-derived Kit references before creating the candidate.

Certification checks host settings, the frozen fixture, Kit artifacts/contracts and
agent availability/evidence before preparing containers. Linux tooling and offline
metric checks precede compatibility and integration scenarios; missing agent evaluations
run automatically and save resumable results. Final evidence validation remains last.
Backend/Frontend regression and full coverage are separate manual checks. See the
[execution order](../quality/external-consumer/README.md#execution-order) for phases,
saved agent evidence and checkpoint reuse.

For a transient certification failure, retain that directory and rerun
`node quality/external-consumer/release.mjs certify artifacts/release-candidate`.
Successful stages resume only after input, environment and evidence hashes match.
Do not repack or reseal merely to retry a failed download. The runner prepares exact
private host tools and required images first; see the maintainer runbook for its
checkpoint and cache rules.

For application creation from these files, see
[Before publication](../docs/architecture/external-app-upgrades.md#before-publication).
For complete local certification, follow the
[maintainer runbook](../quality/external-consumer/README.md). Local packaging alone
does not certify or publish the candidate.

## Check local containers before publication

Use these checks after preparing the local package set above, especially when
changing Dockerfiles, shared UI configuration or `.dockerignore`. They cover a
different boundary from the generated starter's Compose certification.

From the repository root, with the corresponding `.env` files configured:

```sh
(
  set -e

  for vertical in crm pm trade ab; do
    docker compose \
      --env-file ".env.${vertical}" \
      -f "docker-compose.${vertical}.yml" \
      build
  done
)
```

This builds images without starting services or applying migrations. Local CRM
builds use `artifacts/nuget` and `artifacts/npm/ngbplatform-ui-local.tgz`; release
CRM builds restore from public registries. CRM's Tailwind configuration imports
`@ngbplatform/ui/tailwind-preset` from the package, so its image does not need the
platform source directory or `tailwind.shared.config.js`.
The root `.dockerignore` retains the two package feeds while excluding other root
`artifacts` contents and `.env` files from the build context.

For a configured development CRM database, start the stack and inspect readiness:

```sh
docker compose --env-file .env.crm -f docker-compose.crm.yml -p ngb-crm up -d
docker compose --env-file .env.crm -f docker-compose.crm.yml -p ngb-crm ps -a
docker compose --env-file .env.crm -f docker-compose.crm.yml -p ngb-crm logs ngb.crm.migrator
```

Unlike `build`, `up` runs the configured Migrator. Expect its exit code to be zero,
then use the web/API ports from `.env.crm` to check login, a saved business record,
roles/access, audit and Work Center. Exercise Notes and Attachments when enabled.
Use the [application creation flow](../docs/architecture/external-app-upgrades.md#before-publication)
to check the starter separately. Stop a manually started starter before certification
so its ports 5180–5185 are available.

After the final source change, certify a new candidate using the
[complete local sequence](../quality/external-consumer/README.md#run-a-new-candidate).
Do not reuse a previous pass as evidence for changed source. Local checks cannot
verify OIDC approval, registry publication, GHCR push or the infrastructure PR;
those must succeed in the release workflows.

## When to run full quality locally

`run-full-quality.sh` runs backend/frontend coverage, the framework browser matrix,
tooling tests and performance-tooling/metric regression checks. This is an optional
manual aggregate. FULL Certification does not run it, platform/application Backend
tests, `npm run test:all`, framework browser suites or either full-coverage script.
The maintainer runs regression and coverage independently; certification does not
require or import those reports. It runs the five external-consumer profiles,
compatibility, package-only MCP, tooling checks and five AI Development Kit profiles.

Application developers run their own application's tests. Platform contributors can
run the aggregate before opening a PR to catch failures sooner. Use prepared local
artifacts from [Prepare local artifacts](#prepare-local-artifacts).
On Linux, install .NET 10, Node 24.19.x, k6 1.2.2 and the browser dependencies, then:

```sh
npm --prefix ui ci
npm --prefix quality ci
cd ui
npx playwright install --with-deps chromium firefox webkit
cd ..
bash run-full-quality.sh
```

The direct shell command uses host tools and does not prepare the pinned environment
or resume completed stages. On macOS or Linux with a local Unix Docker socket,
the wrapper runs the aggregate in the pinned Linux environment and can resume its
completed stages:

```sh
node quality/external-consumer/full-quality.mjs artifacts/release-candidate
```

The wrapper requires a sealed candidate; it does not package one. It verifies its
inputs and copies source and artifacts into a private workspace without host caches.
That workspace is retained for retries. Tooling is run once per unchanged aggregate,
with separate checkpoints for backend, frontend, browser and performance gates.
The direct wrapper remains an explicit manual operation. Release certification
uses only its `--certification --preflight` mode for tooling and offline performance
checks, producing candidate-bound `quality-preflight.json`. It does not require
`full-quality.json` or backend/frontend coverage reports. Historical matrices retain
their original browser gates and `certification-quality.json` evidence.
For the complete local certification sequence, see the
[certification maintainer runbook](../quality/external-consumer/README.md).

## Artifact contract

`packaging/nuget/projects.txt` and `baselines.json` define the package inventory.
Version 3.4.0 produces 32 NuGet packages (31 runtime packages and one template),
31 symbol packages and two npm archives (`@ngbplatform/ui` and `@ngbplatform/cli`).

Certification seals package hashes, npm integrity, NuGet payload identities,
generated lockfiles, source identity, the frozen fixture identity and the exact
matrix. Publication downloads that artifact set without packing again. NuGet
repository signing can change the outer archive; registry verification checks
signatures and canonical payload contents. npm must preserve the certified integrity.

A failed or missing gate blocks release. Partial publication may be retried with
the identical certified files. A defective or different immutable published package
requires a new version. Expired artifacts cannot be replaced silently by a new pack.

## SemVer and API compatibility

`Directory.Build.props` supplies the NuGet target version. UI, template and CRM
dependencies, documentation, release-tooling and performance workspace versions
and their lockfiles must agree with it, as must `quality/external-consumer/matrix.json`
and `packaging/nuget/baselines.json`. Historical fixtures and third-party dependency
versions keep their own versions.

Every runtime package is validated against its first stable release in the major
line and the selected previous supported release. For the registered 3.3.0 → 3.4.0
transition, most major baselines are 3.0.0; Attachments, Notes and Attachments.MinIO
start at 3.1.0. MCP and AI Assistant packages were first published in 3.3.0 and use
3.3.0 as both their first-major and previous-release baseline. Templates were first
published in 3.2.0 and contain no runtime assembly. These package-specific exceptions
are explicit in `baselines.json`.

SDK package validation and ApiCompat stay enabled. Strict baseline equality is
disabled to permit compatible additions; attribute and parameter-name checks stay
enabled. Do not hide minor/patch breaks with suppressions. Incompatible public
changes require a new major version and a migration guide.

`NgbPlatformAssemblyVersion` remains 3.0.0.0 within the 3.x line; file and
informational versions identify the exact build. Certification also compares npm
entry points, declarations, peer contracts, CSS tokens and assets with the published
source version.

The top-level UI export snapshot can be checked independently:

```sh
npm --prefix ui run test:api-compat
```

Snapshot regeneration with `npm --prefix ui run update:api-compat` needs review;
it does not make an incompatible change acceptable in a minor release.
