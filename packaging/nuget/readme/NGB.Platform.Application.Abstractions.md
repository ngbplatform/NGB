# NGB.Platform.Application.Abstractions

Application-facing service interfaces for composing NGB Platform hosts and extensions.

## Install

```bash
dotnet add package NGB.Platform.Application.Abstractions
```

## What It Contains

- Service contracts for documents, catalogs, audit log, reporting, menu contribution, metadata contribution, report variants, and UI effects.
- Interfaces used by ASP.NET Core hosts and vertical modules to interact with the platform runtime.

## Notes

Use this package for dependency boundaries where code should depend on NGB application services without taking a direct dependency on concrete runtime implementations.

