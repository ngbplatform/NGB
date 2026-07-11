# NGB.Platform.OperationalRegisters

Operational register contracts and primitives for NGB Platform.

## Install

```bash
dotnet add package NGB.Platform.OperationalRegisters
```

## What It Contains

- Operational register identifiers, naming, periods, resources, dimensions, movement contracts, and projection contracts.
- Validation and schema-related exceptions.

## Notes

Use this package for append-only operational state modeling. Concrete storage and schema management live in provider packages such as `NGB.Platform.PostgreSql`.

