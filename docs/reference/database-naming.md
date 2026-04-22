---
title: Database Naming Quick Reference
description: Concise naming quick reference for PostgreSQL schemas, tables, constraints, and migration artifacts used in NGB.
---

# Database Naming Quick Reference

Use this page as the short lookup version of the fuller [Database Naming and DDL Patterns](/reference/database-naming-and-ddl-patterns) chapter.

## Core rules

| Area | Preferred pattern | Example |
|---|---|---|
| General object names | lowercase `snake_case` | `platform_users` |
| Platform-owned tables | `platform_<meaning>` | `platform_users` |
| Document head tables | `doc_<domain>_<type>` | `doc_trd_sales_invoice` |
| Document part tables | `doc_<domain>_<type>__<part>` | `doc_trd_sales_invoice__lines` |
| Catalog tables | `cat_<domain>_<type>` | `cat_trd_customer` |
| Operational registers | `or_<domain>_<register>` | `or_trd_inventory` |
| Reference registers | `rr_<domain>_<register>` | `rr_trd_pricing` |
| Views | `v_<meaning>` or `<module>_<meaning>_v` | `v_open_receivables` |
| Materialized views | `mv_<meaning>` | `mv_monthly_balances` |
| Functions | `fn_<meaning>` | `fn_close_month` |
| Procedures | `sp_<meaning>` if that convention is used locally | `sp_rebuild_balances` |

## Constraint and index patterns

| Object | Preferred pattern | Example |
|---|---|---|
| Foreign key | `fk_<table>__<referenced_table>` | `fk_pm_receivable_payment__pm_party` |
| Unique constraint/index | `uq_<table>__<business_key>` | `uq_trd_item_price__item_price_type_currency_effective_from` |
| Non-unique index | `ix_<table>__<important_columns>` | `ix_accounting_entries__entity_id_posting_date` |
| Check constraint | `ck_<table>__<rule>` | `ck_document_number_sequences__last_seq_positive` |
| Trigger | `trg_<table>__<purpose>` | `trg_document_relationships__mirror_reverse_edge` |

## Migration file names

| Kind | Preferred pattern | Example |
|---|---|---|
| Versioned migration | `Vyyyy_mm_dd_nnnn__description.sql` | `V2026_04_18_0001__add_trd_business_partner.sql` |
| Repeatable migration | `R__description.sql` | `R__rebuild_reporting_helpers.sql` |

## Practical rules

- Prefer business meaning over UI labels or temporary implementation names.
- Keep platform and vertical prefixes explicit so ownership is obvious.
- Use stable names that will remain readable once SQL files become embedded resource names.
- When in doubt, prefer consistency with adjacent tables and existing packs over novelty.

## Related pages

- [Database Naming and DDL Patterns](/reference/database-naming-and-ddl-patterns)
- [PostgreSQL](/platform/postgresql)
- [Platform document persistence model](/platform/platform-document-persistence-model)
