# Publishing NGB Platform Packages

NGB Platform publishes two public package families:

- `NGB.Platform.*` on nuget.org.
- `@ngbplatform/ui` on npmjs.com.

PM, Trade, and Agency Billing continue to build from platform source projects and the local
`ngb-ui-framework` npm workspace. CRM is the first vertical that consumes only published platform
packages in release builds.

## Validate Locally

```bash
bash packaging/nuget/pack-platform.sh 1.3.0
npm --prefix ui run pack:platform-ui -- --version 1.3.0
docker compose -f docker-compose.crm.yml --env-file .env.crm build ngb.crm.web
```

Generated packages are written below `artifacts/` and are ignored by Git.

For the first npm release only, generate the CRM registry lock from the reviewed deterministic tarball:

```bash
npm --prefix ui run lock:crm-web:bootstrap
```

After `@ngbplatform/ui@1.3.0` is public, refresh and verify the same lock through the registry:

```bash
npm --prefix ui/ngb-crm-web install --package-lock-only
git diff --exit-code -- ui/ngb-crm-web/package-lock.json
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

Create a nuget.org trusted publishing policy with:

- Repository owner: `ngbplatform`
- Repository: `NGB`
- Workflow: `publish-platform-nuget.yml`
- Environment: `nuget`

Create the GitHub environment `nuget` and add `NUGET_USER` containing the nuget.org profile name.
The workflow exchanges the GitHub OIDC token for a short-lived API key through `NuGet/login`.

## Release Order

1. Run `platform-packages` and review both package artifacts.
2. Publish `NGB.Platform.*`.
3. Publish `@ngbplatform/ui`.
4. Run `crm-container-images` after both registries expose version `1.3.0`.

The CRM release workflow restores with `NuGet.Registry.Config` and its own npm lockfile, so local package
outputs cannot leak into production images.

Set the repository variable `NGB_CRM_RELEASE_ENABLED=true` after both package families are public.
Until then, automatic CRM image builds remain gated; `workflow_dispatch` is still available for an
intentional verification run.
