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
npm --prefix ui run pack:platform-ui
docker compose -f docker-compose.crm.yml --env-file .env.crm build ngb.crm.web
```

Generated packages are written below `artifacts/` and are ignored by Git.

The CRM lockfile is resolved from npmjs.com and is not regenerated while validating or publishing the
platform package. Update it only when CRM intentionally moves to another published package version:

```bash
npm --prefix ui/ngb-crm-web install --save-exact @ngbplatform/ui@1.3.1
```

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
on `main`. The workflow packs the platform projects, verifies all 16 packages and symbol packages,
then publishes in dependency order with `--skip-duplicate` so a partially completed run can be retried.

## Release Order

1. Run `platform-packages` and review both package artifacts.
2. Publish `NGB.Platform.*`.
3. Publish `@ngbplatform/ui`.
4. Run the CRM image jobs in `container-images` after NuGet and npm expose version `1.3.1`.

The CRM release workflow restores with `NuGet.Registry.Config` and its own npm lockfile, so local package
outputs cannot leak into production images.

Set the repository variable `NGB_CRM_RELEASE_ENABLED=true` after both package families are public.
Until then, automatic CRM image builds remain gated; `workflow_dispatch` is still available for an
intentional verification run.
