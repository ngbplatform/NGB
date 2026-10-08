# Published 3.1.0 source fixture

This independent application was authored and run against the public 3.1.0
NuGet/npm packages before any 3.2.0 production changes. It is not generated
from the target template. Its own Tailwind configuration is intentional:
3.1.0 did not export a public preset.

The fixture has a real catalog definition, validation/orchestration in Runtime,
and PostgreSQL persistence/migration in the provider project. Hosts compose the
modules; Runtime never references PostgreSql. The Probe is certification tooling,
not part of the proposed starter workload.

Use only this directory in an isolated work directory. Restore with the checked-in
NuGet.Config and lockfiles, using an empty package cache. Run `npm ci --workspaces=false`
in web. Build `CertificationApp.slnx` in Release and `npm run build` in web.
Run migrations before starting either host. The runtime needs the connection
string, Keycloak issuer/client IDs and the Keycloak admin service-client settings.
No credentials are checked in; the infrastructure file requires private environment
values. Local HTTP and Keycloak start-dev are strictly local test settings.

The supplied realm has a trusted ngb-admin identity. Its initial login does not
require a platform-user record. Through the users API/UI, link its existing email
to a platform user and assign the pre-seeded certification.administrator role.
This also enables personal Work Center features, which require a linked user.
Username editing is enabled because the platform synchronizes username and email.

The default feature flags disable Notes and Attachments. Dedicated certification
profiles enable Notes alone, or Notes plus Attachments with private MinIO storage.
The database, realm, Hangfire schema and object storage must survive an upgrade.
Never reseed them between source and target.

Freeze hashes and registry provenance are recorded outside this directory.
Changes to this source fixture require explicit re-baselining; an upgrade patches
an isolated copy and must preserve the original.
