# Publishing NGB Platform Packages

NGB publishes `NGB.Platform.*` on nuget.org and `@ngbplatform/ui` on npmjs.com.
The template is included in the NuGet family. PM, Trade and Agency Billing build
from platform source; CRM release images restore platform packages from registries.

This maintainer runbook covers release approval, trusted publishing, container
deployment and retries.
The normal configured sequence is:

**PR → checks → merge → main certification → approval → package publication and
registry verification → container builds → infrastructure deployment PR.**

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

Open [@ngbplatform/ui](https://www.npmjs.com/package/@ngbplatform/ui) → **Settings** →
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
Actions display name `publish-certified-platform-release`. Stage-only permission
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
UI, template and CRM dependencies, the certification matrix, package baselines and
registered CLI transition. Record changes and migration instructions. A repeated
publication of an existing version is only a retry of the identical certified set.
Prepare local candidate packages using [Prepare local artifacts](#prepare-local-artifacts),
then review and commit the generated template and CRM lockfiles. The strict npm
job and CRM image build consume the committed CRM lockfile; container jobs do not
replace it with the copy saved in certification evidence. A changed UI archive
therefore requires regenerating and committing its matching lockfile before merge.

| Step | What you do | What to check |
| --- | --- | --- |
| 1 | Commit and push the feature branch. In GitHub, open a PR targeting `main`. | The PR includes the intended version, migration documentation and source changes. |
| 2 | Open the PR's **Checks** tab and wait. | `external-app-certification` passes, plus `platform-packages` when its path filters trigger, and other required checks. |
| 3 | Review and merge the PR in GitHub. | Do not run the old publish workflows from the feature branch. |
| 4 | Open **Actions → external-app-certification** and select the new `main` run. | It passes for the merged commit and uploads `ngb-certified-release`. PR certification does not replace this run. |
| 5 | Open **Actions → publish-certified-platform-release**, select the matching run and inspect its summary. | Commit, certification run and artifact identify the release you intend to publish. |
| 6 | Click **Review deployments**, select `platform-release`, then **Approve and deploy**. | The publication job succeeds, including registry verification and promotion evidence. |
| 7 | Open **Actions → container-images** and wait for the automatically triggered run. | Release evidence, CRM registry checks, all 20 image builds and `propose infra deploy` succeed. |
| 8 | Open the generated PR in `ngbplatform/ngb-platform-infra`. Review its image tags and merge it. | All four workloads reference `sha-<released-commit>`. Check the deployment through your configured Argo CD process. |

No run IDs or artifact IDs are entered manually. No local quality command is
required to trigger this flow. Approval authorizes package publication; the
infrastructure PR is a separate deployment review. Registry publication alone does
not mean an application has been deployed.

The workflows do not create a Git tag or GitHub Release entry. When recording a
GitHub Release, create/select the tag at the verified release commit and publish
the matching changelog after package verification succeeds. Do not tag a newer
`main` commit just because it is now the branch tip.

## What each workflow does

| Actions name | Purpose |
| --- | --- |
| `platform-packages` | Package/API checks for matching changed paths; its artifacts do not authorize release. |
| `external-app-certification` | Build the candidate, prepare lockfiles, seal it and run all required profiles and quality gates. |
| `publish-certified-platform-release` | Select successful main certification, wait for approval, publish its saved packages and verify registry contents/runtime. |
| `container-images` | Verify publication evidence, build the released commit and propose the infrastructure update. |

In `platform-packages`, the NuGet job waits for successful npm validation and
downloads that job's archive by artifact ID into `artifacts/npm` before packing.
Template lockfile generation consumes the same archive; it does not repack the UI.
`external-app-certification` packs npm before NuGet within its single job.

The certified artifact is retained for 30 days and publication evidence for 90 days
under the checked-in workflow settings. Downloads select the exact artifact ID,
not an arbitrary artifact with the same name.

The manifest preserves the generated template and CRM lockfiles. They are derived
from the package bytes during packing; use that generation step before the PR,
not manual integrity edits. Registry verification checks NuGet content/signatures and npm
integrity. Source, image tags and deployment PR all identify the released commit.

An ordinary push does not publish images. A vertical-only change needs publication
evidence for its exact commit to enter this release train; independent vertical
release pipelines are outside this workflow's scope.

## Retry a failed stage

| Failure | Action |
| --- | --- |
| PR or main certification failed | Inspect the failed step. Fix source/test failures in a PR; rerun a transient failure for the same commit. Nothing has been published yet. |
| Release selection reports no reviewer or an OIDC policy mismatch | Correct the external settings, then rerun the failed publication job. Check workflow filename and environment first. |
| Upload or registry verification failed transiently | Rerun the original publication run, retaining the same certified artifact. Existing npm integrity must match; NuGet duplicates are checked during registry verification. |
| Published contents are wrong or differ from the certified package | Prepare a corrected new version. Published versions cannot be overwritten. |
| Container build or infrastructure PR creation failed | Repair external credentials/infrastructure as needed and rerun that release's container run. It continues to use the verified release commit. |
| Certification artifact expired | Re-certification is required. Do not replace a partially published candidate with newly packed bytes of the same version. |

For manual dispatch, select **Run workflow → main**. Publication requires successful
main certification for that exact dispatch commit; container dispatch requires
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
`pack-platform.sh` calls `packaging/templates/prepare-locks.mjs` before packing the
template. It regenerates template NuGet lockfiles, template npm integrity and CRM
npm integrity from the candidate. Review and commit the derived lockfile changes
before opening the release PR: CRM image jobs consume the committed lockfile.

`--local-candidate` is also used by certification CI. It permits packing the UI
before the derived lockfiles are generated; it does not waive the later artifact,
compatibility or registry checks. Without that flag, UI packaging requires the
existing CRM lockfile to match the packed archive exactly. Do not hand-edit its
integrity or introduce a second manual npm installation procedure.

Artifacts are ignored by Git. The default sealed directory is
`artifacts/release-candidate`; it must not exist before `seal`. For another attempt,
use a new path, for example `release.mjs seal artifacts/release-candidate-next`,
and pass that path to subsequent commands. Source changes after sealing, including
documentation changes, invalidate source identity for certification.

For application creation from these files, see
[Before publication](../docs/architecture/external-app-upgrades.md#before-publication).
For complete local certification, follow the
[maintainer runbook](../quality/external-consumer/README.md). Local packaging alone
does not certify or publish the candidate.

## When to run full quality locally

`run-full-quality.sh` runs backend/frontend coverage, the framework browser matrix,
tooling tests and performance-tooling/metric regression checks. It does not publish
packages, run every live k6 load profile or independently perform the complete
five-profile external upgrade certification. Release certification includes this
aggregate and the external profiles.

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

On macOS with a local Unix Docker socket, the wrapper runs the same aggregate in Linux:

```sh
node quality/external-consumer/full-quality.mjs artifacts/release-candidate
```

The wrapper requires the candidate NuGet/npm directories; it does not package them.
It copies source and artifacts into a temporary workspace without host caches.
For the complete local certification sequence, see the
[certification maintainer runbook](../quality/external-consumer/README.md).

## Artifact contract

`packaging/nuget/projects.txt` and `baselines.json` define the package inventory.
The current target produces 24 `.nupkg` files, 23 runtime `.snupkg` files and one
versioned npm archive. `NGB.Platform.Templates` contains no runtime assembly or symbols.

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
dependencies must agree with it, as must `quality/external-consumer/matrix.json`
and `packaging/nuget/baselines.json`.

Every runtime package is validated against its first stable release in the major
line and the selected previous supported release. For the registered 3.1.0 → 3.2.0
transition, most major baselines are 3.0.0; Attachments, Notes and Attachments.MinIO
start at 3.1.0. The first-release template exception is explicit in `baselines.json`.

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
