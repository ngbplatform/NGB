# NGB.Platform.Core

Core NGB Platform primitives shared across runtime, persistence, definitions, and host packages.

## Install

```bash
dotnet add package NGB.Platform.Core
```

## What It Contains

- Document, catalog, audit log, security, reporting, lock, and dimension primitives.
- Stable platform value objects and records.
- Common domain concepts that are intentionally below runtime orchestration.

## Notes

Use this package when building platform extensions that need durable NGB concepts but should not depend on runtime execution or concrete persistence.

