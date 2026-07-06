# NGB.Platform.PostgreSql

PostgreSQL provider implementations and migration support for NGB Platform.

## Install

```bash
dotnet add package NGB.Platform.PostgreSql
```

## What It Contains

- PostgreSQL readers, writers, repositories, and unit-of-work implementation.
- Dapper/Npgsql integration.
- Evolve migration support and embedded platform schema migration resources.
- Reporting SQL execution and PostgreSQL dataset support.
- Dependency injection extensions such as `AddNgbPostgres(...)`.

## Notes

Migration SQL files are embedded in `NGB.PostgreSql.dll` and discovered by the NGB migrator infrastructure.

