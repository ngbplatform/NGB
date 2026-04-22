---
title: Catalogs
---

# Catalogs

Catalogs are first-class platform entities used for master data and other reusable business reference entities.

Examples include:

- customers;
- vendors;
- business partners;
- items;
- warehouses;
- properties;
- bank accounts;
- maintenance categories;
- price types.

## What a catalog is in NGB

A catalog is more than just a table and CRUD controller.

A production-ready catalog in NGB usually includes:

- a stable catalog code;
- metadata for fields and UI behavior;
- lifecycle and action behavior;
- provider-agnostic Runtime orchestration;
- PostgreSQL persistence;
- optional validation and policy rules;
- auditability;
- reportability and drilldown integration.

## Why catalogs are platform concerns

Master data is central to business systems, and NGB wants catalogs to behave consistently across verticals.

That means catalogs should share:

- list and editor behavior;
- metadata-driven field definitions;
- display conventions;
- deletion semantics;
- API patterns;
- audit and explainability support.

## Normal catalog lifecycle

A catalog row is usually created, updated, and optionally marked for deletion.

The important detail is that “delete” in a production-minded business system usually means a controlled lifecycle state, not an immediate physical delete.

That approach helps preserve referential integrity, auditability, and safe administrative cleanup flows.

## What belongs in catalog definitions

A catalog definition should clearly answer:

- what the catalog code is;
- what fields exist;
- what labels and UI metadata apply;
- what validations apply;
- which actions are allowed;
- how display text is produced or exposed.

## Typical catalog responsibilities

A catalog usually owns:

- the identity of the entity;
- core descriptive fields;
- business classification fields;
- active / deleted semantics;
- metadata for forms and lists.

A catalog should **not** own unrelated transactional history. That belongs in documents, registers, or reporting.

## Catalogs and display values

In NGB, user-facing display text matters. Users should see meaningful display strings rather than opaque GUIDs.

A clean catalog experience should therefore expose:

- human-readable labels;
- predictable list columns;
- consistent status / deletion indicators;
- drilldown-friendly display values in reports and relationships.

## Catalogs and auditability

Catalog changes matter in business systems. A change to a customer, item, or warehouse can affect downstream flows.

That is why catalog actions should remain auditable and predictable.

## Recommended design mindset

Treat each catalog as a durable business concept, not as a random storage bucket.

Good catalog design asks:

- will users understand this entity consistently across UI, reports, and documents?
- are we giving it a stable code and clean display model?
- does the entity have the right boundary, or are we mixing unrelated concerns?

The implementation guide later in this documentation shows one clean way to add a new catalog in a vertical solution.
