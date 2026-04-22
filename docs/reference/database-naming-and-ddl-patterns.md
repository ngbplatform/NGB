---
title: "Database Naming and DDL Patterns"
description: "Recommended naming patterns for tables, heads, parts, and migration-oriented database design in NGB."
---

# Database Naming and DDL Patterns

> **Page intent**
> This page captures the practical naming conventions and DDL patterns implied by the verified source and by the existing NGB platform structure.

## Trust level

- **Verified anchors:** `NGB.Runtime/Documents/DocumentService.cs`, `NGB.PostgreSql/NGB.PostgreSql.csproj`, `NGB.Migrator.Core/PlatformMigratorCli.cs`
- **Architecture synthesis:** yes
- **Template guidance:** yes

## Chapter navigation

- [PostgreSQL](/platform/postgresql)
- [PostgreSQL Source Map](/platform/postgresql-source-map)
- [Migrator Deep Dive](/platform/migrator-deep-dive)
## Verified source anchors

### Head/part document table convention is explicit in runtime comments

Confirmed in:

- `NGB.Runtime/Documents/DocumentService.cs`

Verified statement from the runtime comments and behavior:

- head table pattern: `doc_*`
- part table pattern: `doc_*__*`
- persistence model: common registry + typed head table

### PostgreSQL provider embeds SQL migrations as resources

Confirmed in:

- `NGB.PostgreSql/NGB.PostgreSql.csproj`

Verified behavior:

- SQL migrations under `db/migrations/**/*.sql` are embedded resources.

### Migrator is designed around migration packs and deterministic discovery

Confirmed in:

- `NGB.Migrator.Core/PlatformMigratorCli.cs`

## Recommended naming model

### Documents

- common document registry: platform-level common table(s)
- typed head tables: `doc_<domain>_<type>`
- typed part tables: `doc_<domain>_<type>__<part>`

Examples for an American-market vertical:

- `doc_trd_sales_order`
- `doc_trd_sales_order__lines`
- `doc_trd_price_update`

### Catalogs

Recommended pattern:

- `cat_<domain>_<type>`

Examples:

- `cat_trd_customer`
- `cat_trd_product`
- `cat_trd_warehouse`

### Operational registers

Recommended pattern:

- `or_<domain>_<register>`
- supporting totals/turnover/effective objects may use suffixes that make read purpose explicit

Examples:

- `or_trd_inventory`
- `or_trd_settlement`

### Reference registers

Recommended pattern:

- `rr_<domain>_<register>`

Examples:

- `rr_trd_pricing`
- `rr_tax_sales_rules`

## DDL principles

- Prefer explicit, stable names.
- Keep platform/vertical prefixes readable.
- Avoid opaque generated names for core business tables.
- Make migration scripts deterministic and pack-friendly.
- Design for append-only history where the domain requires auditability.

## Related pages

- [Migrator Deep Dive](/platform/migrator-deep-dive)
- [Documents, Flow, Effects, Derive](/platform/documents-flow-effects-deep-dive)
