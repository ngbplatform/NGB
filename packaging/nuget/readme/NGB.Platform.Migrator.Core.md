# NGB.Platform.Migrator.Core

Shared schema migrator CLI engine for NGB Platform.

## Install

```bash
dotnet add package NGB.Platform.Migrator.Core
```

## What It Contains

- Migration-pack discovery and dependency planning.
- Evolve migration execution through the PostgreSQL provider.
- Repair, validation, dry-run, list-modules, and information modes.
- Schema locking and CLI exit code conventions.

## Notes

This package is intended to be referenced by application-specific migrator hosts. It is not intended to run implicitly from API startup in production.

