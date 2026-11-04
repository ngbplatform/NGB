# Application schema migration fixtures

Baseline contains the four application migrations before the checklist was added.
They were extracted from the verified template artifact identified in
`baseline.json`; its hashes freeze their original bytes. This is a previous
application schema revision on SDK 3.4.0, not a claim of an SDK 3.3.0 upgrade.

ChecksumDrift deliberately changes one applied script. It exists only to verify
checksum rejection and continued usability after a failed migration attempt.
Never copy it into an application's migration pack or repair an applied history
to accept it.

Both projects contain resources only and declare no migration contributors.
Normal Migrator discovery therefore does not include either fixture. Tests select
their assemblies explicitly through the public MigrationPack contract.
