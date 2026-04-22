---
title: Guide · Business Partners Catalog
---

# Guide · Add a Business Partners Catalog

This guide shows one clean, production-ready way to add a new catalog in a vertical solution.

The example uses an American-market entity name: **Business Partners**. A Business Partner can represent a customer, vendor, or both, depending on flags or roles.

> This guide shows a recommended vertical structure and code shape. Adapt namespaces, folders, and exact builder APIs to your solution, but keep the architectural responsibilities the same.

## Goal

We want a reusable master-data catalog that can be used by:

- Sales Invoice;
- Purchase Receipt;
- Customer Payment;
- Vendor Payment;
- reports and lookups.

## Step 1: Decide the catalog boundary

A good Business Partners catalog usually owns:

- legal name;
- display name;
- tax identifier;
- email / phone;
- billing address;
- shipping address;
- customer flag;
- vendor flag;
- active status.

Do **not** put transactional balances or operational history in the catalog itself.

## Step 2: Choose the vertical code and file layout

A clean example layout for a Trade vertical could look like this:

```text
NGB.Trade.Contracts/
  Catalogs/BusinessPartners/
    BusinessPartnerDto.cs
    BusinessPartnerListItemDto.cs

NGB.Trade.Definitions/
  Catalogs/BusinessPartners/
    TrdBusinessPartnerCatalogDefinition.cs

NGB.Trade.Runtime/
  Catalogs/BusinessPartners/
    BusinessPartnerRules.cs

NGB.Trade.PostgreSql/
  Bootstrap/db/migrations/
    V2026_04_18_0001__add_trd_business_partner.sql
```

## Step 3: Define the DTOs

```csharp
namespace NGB.Trade.Contracts.Catalogs.BusinessPartners;

public sealed class BusinessPartnerDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string LegalName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsCustomer { get; init; }
    public bool IsVendor { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? TaxId { get; init; }
    public bool IsActive { get; init; }
}
```

## Step 4: Add the catalog definition

The definition should declare:

- catalog code;
- label;
- fields;
- UI metadata;
- list columns;
- deletion semantics;
- validations or hooks if needed.

```csharp
public sealed class TrdBusinessPartnerCatalogDefinition : CatalogDefinition
{
    public const string Code = "trd.business_partner";

    public override string CatalogCode => Code;
    public override string DisplayName => "Business Partners";

    public override IReadOnlyList<FieldDefinition> Fields => new FieldDefinition[]
    {
        Field.Text("code", "Code", required: true),
        Field.Text("legal_name", "Legal Name", required: true),
        Field.Text("display_name", "Display Name", required: true),
        Field.Bool("is_customer", "Customer"),
        Field.Bool("is_vendor", "Vendor"),
        Field.Text("email", "Email"),
        Field.Text("phone", "Phone"),
        Field.Text("tax_id", "Tax ID"),
        Field.Bool("is_active", "Active")
    };
}
```

The exact API in your codebase may differ, but the responsibility is the same: the definition declares the business shape of the catalog.

## Step 5: Add the PostgreSQL migration

```sql
create table if not exists trd_business_partner
(
    id uuid primary key,
    code text not null,
    legal_name text not null,
    display_name text not null,
    is_customer boolean not null default false,
    is_vendor boolean not null default false,
    email text null,
    phone text null,
    tax_id text null,
    is_active boolean not null default true,
    is_deleted boolean not null default false,
    created_utc timestamp with time zone not null,
    modified_utc timestamp with time zone not null
);

create unique index if not exists uq_trd_business_partner__code
    on trd_business_partner (code);

create index if not exists ix_trd_business_partner__display_name
    on trd_business_partner (display_name);
```

## Step 6: Add business rules

Examples of good catalog rules:

- at least one of `is_customer` or `is_vendor` must be true;
- `code` must be unique;
- deleted rows cannot be selected by default in operational lookups.

```csharp
public static class BusinessPartnerRules
{
    public static void Validate(BusinessPartnerDto dto)
    {
        if (!dto.IsCustomer && !dto.IsVendor)
            throw new NgbArgumentException("A business partner must be a customer, a vendor, or both.");
    }
}
```

## Step 7: Expose it in metadata-driven UI

Once the definition exists and the Runtime / PostgreSQL paths are wired, the platform can expose:

- list page;
- create / edit page;
- lookup field usage in documents;
- drilldowns from reports.

That is one of the main benefits of the platform: a new catalog becomes available through shared conventions instead of needing a custom CRUD page from scratch.

## Step 8: Use the catalog from a document

A Sales Invoice should reference the Business Partner through the document payload or head, not by embedding duplicated customer fields everywhere.

That keeps master data and transactional data separated cleanly.

## Step 9: Audit and deletion semantics

Make sure catalog actions remain auditable and that “delete” means the standard business-safe semantics used by the platform.

That usually means:

- mark for deletion or deleted flag in normal user flows;
- preserve referential safety;
- keep administrative hard deletion as a separate, deliberate operation if supported.

## Checklist

Before considering the new catalog done, verify:

- the migration is applied;
- the definition appears in metadata and UI;
- create, update, and delete semantics are correct;
- lookups show human-readable display text;
- reports and drilldowns can resolve the entity cleanly;
- audit history is visible.

This is the standard pattern for adding master data in NGB.
