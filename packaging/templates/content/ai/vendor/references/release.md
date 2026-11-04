# Application delivery and platform package boundaries

Build an independent application with exact, aligned SDK/UI/CLI/Kit versions.
Keep the resolved lockfiles and declared package sources reviewable. Use only
published registry artifacts or the explicitly selected sealed local candidate.
The npm CLI and application-template CLI have one implementation; a second
tooling template or a source checkout is not required.

`validate --profile build` and `test --profile all` execute development tooling.
They do not publish images, packages or deploy a service. Certification readiness
depends on every required gate and exact source/artifact identities, including
applicable performance and real-agent evidence for the Kit release itself.
Do not reuse a passing report after relevant inputs change.

Application deployment remains a separate operation. Review the target, secrets,
TLS/identity configuration, health checks, migration sequence and tested consistent
backup. The existing `deploy --backup-confirmed` flag asserts that backup condition;
it does not create a backup or establish authorization to publish a platform package.

For SDK/Kit changes, use the [coordinated upgrade workflow](upgrades.md). Preserve
application-owned code, configuration and custom guidance. A file rollback does
not reverse a database migration. Test the supported persisted upgrade and recovery
procedure before applying it to a deployment.
