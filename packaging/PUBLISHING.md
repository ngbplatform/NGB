# Publishing NGB Platform Packages

NGB Platform publishes two public package families:

- `NGB.Platform.*` on nuget.org.
- `@ngbplatform/ui` on npmjs.com.

PM, Trade, and Agency Billing continue to build from platform source projects and the local
`ngb-ui-framework` npm workspace. CRM is the first vertical that consumes only published platform
packages in release builds.

## Validate Locally

```bash
bash packaging/nuget/pack-platform.sh
bash packaging/nuget/verify-platform-packages.sh
npm --prefix ui ci
npm --prefix ui run test:api-compat
npm --prefix ui run pack:platform-ui
docker compose -f docker-compose.crm.yml --env-file .env.crm build ngb.crm.web
```

Generated packages are written below `artifacts/` and are ignored by Git.

These shell commands target macOS/Linux. For local Windows builds, use the
[PowerShell and Linux-container alternatives](../docs/start-here/run-locally.md#prepare-local-platform-packages).
Release workflows run on Linux and require strict candidate validation.

For testing unpublished UI changes locally, use `npm --prefix ui run pack:platform-ui -- --local-candidate`.
This builds the candidate without requiring its integrity to match the published CRM package.
It preserves version/registry-reference validation and leaves the CRM lockfile unchanged.
Release validation must use the command without this flag.

The CRM lockfile must describe the exact release candidate: version, registry URL, dependency
metadata, and SHA-512 integrity. Packaging and publishing validate this lockfile; they do not
regenerate it. Prepare it when accepting a new release candidate, then verify it against the
published package. An integrity mismatch must be resolved before the strict release workflows pass;
`--local-candidate` is only a local-development bypass. Never replace an already published version
with different content.

For a new version that is not yet in the registry:

1. Build the tarball with `npm --prefix ui run pack:platform-ui -- --local-candidate`.
2. From `ui/ngb-crm-web`, run
   `npm install --package-lock-only --workspaces=false --ignore-scripts --save-exact ../../artifacts/npm/ngbplatform-ui-3.0.0.tgz`.
3. Replace only the temporary file references: set `dependencies["@ngbplatform/ui"]` in
   `package.json` and `packages[""].dependencies["@ngbplatform/ui"]` in `package-lock.json` back
   to `3.0.0`; set the locked UI package's `resolved` to
   `https://registry.npmjs.org/@ngbplatform/ui/-/ui-3.0.0.tgz`. Preserve the generated integrity
   and dependency metadata. Do not change the tarball after accepting this candidate.
4. Review the lockfile diff and rerun `npm --prefix ui run pack:platform-ui` from the repository
   root without `--local-candidate`. Commit the reviewed lockfile before publication.

For a package version already available on npmjs.com, update the dedicated consumer lockfile with:

```bash
npm --prefix ui/ngb-crm-web install --workspaces=false --save-exact @ngbplatform/ui@3.0.0
```

## SemVer and API compatibility

`Directory.Build.props` is the canonical version for all `NGB.Platform.*` packages. The
`NgbPlatformApiCompatibilityBaselineVersion` property identifies the first stable package in the
current major line. For 3.x it is `3.0.0`.

Every `dotnet pack` enables the .NET SDK package-validation and ApiCompat rules. Packing `3.0.0`
validates the package itself; packing a later `3.x` release also downloads the published `3.0.0`
package with the same ID and rejects binary/source contract breaks. Do not add ApiCompat
suppressions for a minor or patch release. An intentional incompatible change requires a new major
version, an updated compatibility baseline, changelog breaking-change entries, and a migration
guide.

`NgbPlatformAssemblyVersion` remains `3.0.0.0` for the complete 3.x line so minor and patch package
updates preserve assembly identity. `FileVersion` and `InformationalVersion` continue to identify
the exact build. Change the assembly version only with the next major release.

The `@ngbplatform/ui` top-level export snapshot is checked with:

```bash
npm --prefix ui run test:api-compat
```

Additive or breaking export changes require explicit review and snapshot regeneration with
`npm --prefix ui run update:api-compat`. Updating the snapshot does not make a breaking change
SemVer-compatible; removals and incompatible type changes still require a new major release.

## npm Trusted Publishing

The committed `ngb-ui-framework` directory is the single source for both the local workspace package
and `@ngbplatform/ui`. The packaging script generates the scoped npm manifest in a temporary directory;
it does not duplicate source files in the repository.

After the initial package is created under the `@ngbplatform` npm organization, configure its trusted
publisher with:

- Repository: `ngbplatform/NGB`
- Workflow: `publish-platform-ui.yml`
- Environment: `npm`
- Allowed action: `npm publish`

The workflow uses GitHub OIDC and npm provenance. It does not require an npm automation token.

The first publication is a one-time bootstrap operation because npm trusted publishing is configured
from an existing package's settings. Pack and verify the tarball, publish it interactively with 2FA,
then configure the trusted publisher and disallow token-based publishing.

## NuGet Trusted Publishing

NuGet packages must be published through nuget.org Trusted Publishing, not through stored API keys.
The workflow uses GitHub OIDC to request a short-lived NuGet credential with `NuGet/login@v1`.

Create a nuget.org trusted publishing policy:

- Package owner: `ngb_platform`
- Package ID pattern: `NGB.Platform.*` if the UI asks for one; otherwise the policy applies to the selected owner.
- Trusted publisher: GitHub Actions
- Repository owner: `ngbplatform`
- Repository name: `NGB`
- Workflow filename: `publish-platform-nuget.yml`
- Environment: `nuget`

Create the GitHub environment `nuget` before the first run. Recommended environment settings:

- Deployment branches/tags: `main` and release tags only.
- Required reviewers: enabled for production releases.
- Variables: `NUGET_USER=ngb_platform` if the NuGet owner/profile name changes from the default.
- Secrets: none required for NuGet publishing.

Run `.github/workflows/publish-platform-nuget.yml` with the exact version after the release commit is
on `main`. The workflow packs the platform projects, verifies all 20 packages and symbol packages,
then publishes in dependency order with `--skip-duplicate` so a partially completed run can be retried.

## Release Order

1. Run `platform-packages` and review both package artifacts.
2. Publish `NGB.Platform.*`.
3. Publish `@ngbplatform/ui`.
4. Run the CRM image jobs in `container-images` after NuGet and npm expose version `3.0.0`.

The CRM release workflow restores with `NuGet.Registry.Config` and its own npm lockfile, so local package
outputs cannot leak into production images.

CRM image builds are gated by the live registry dependency check. When the entire NuGet package
set is unavailable, CRM builds are skipped; a partially available dependency set fails the check.
After both package families are published, rerun `container-images` through `workflow_dispatch`.
