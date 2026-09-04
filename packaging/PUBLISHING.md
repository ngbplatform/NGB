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
npm --prefix ui run test:api-compat
npm --prefix ui run pack:platform-ui
docker compose -f docker-compose.crm.yml --env-file .env.crm build ngb.crm.web
```

Generated packages are written below `artifacts/` and are ignored by Git.

The CRM lockfile is resolved from npmjs.com and is not regenerated while validating or publishing the
platform package. Update it only when CRM intentionally moves to another published package version:

```bash
npm --prefix ui/ngb-crm-web install --save-exact @ngbplatform/ui@3.0.0
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

Set the repository variable `NGB_CRM_RELEASE_ENABLED=true` after both package families are public.
Until then, automatic CRM image builds remain gated; `workflow_dispatch` is still available for an
intentional verification run.
