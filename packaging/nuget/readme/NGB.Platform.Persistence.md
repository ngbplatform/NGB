# NGB.Platform.Persistence

Provider-agnostic persistence contracts for NGB Platform.

## Install

```bash
dotnet add package NGB.Platform.Persistence
```

## What It Contains

- Unit-of-work contracts.
- Document and catalog persistence interfaces.
- Accounting, register, reporting, audit log, security, dimension, and maintenance persistence abstractions.

## Notes

Runtime depends on these abstractions. Concrete PostgreSQL implementations are provided by `NGB.Platform.PostgreSql`.

