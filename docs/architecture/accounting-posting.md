---
title: Accounting and Posting
---

# Accounting and Posting

Accounting is not bolted on in NGB. It is a core platform concern.

The platform is built around the idea that a business document is often valuable not only because it stores operational intent, but because it produces durable business effects:

- accounting entries;
- operational register movements;
- reference register movements;
- document relationships;
- audit history.

Posting is the controlled transition from a business document to those effects.

## What posting means in NGB

Posting is the act of taking a valid business document and materializing its effects in append-only storage.

A posted document should answer these questions clearly:

- what happened;
- when it happened;
- who caused it;
- what accounting entries were created;
- what operational or reference state changed;
- how the user can inspect those effects later.

## Typical posting pipeline

The exact implementation varies by document type, but the platform model is stable.

<script setup>
const flowchart = String.raw`sequenceDiagram
    participant UI as UI / API caller
    participant RT as Runtime
    participant DOC as Document service
    participant VAL as Validators / policies
    participant PH as Posting handler(s)
    participant ACC as Accounting engine
    participant OR as Operational Registers
    participant RR as Reference Registers
    participant PG as PostgreSQL

    UI->>RT: Execute Post action
    RT->>DOC: Load document head + payload
    DOC->>VAL: Validate status, workflow, business rules
    VAL-->>DOC: OK
    DOC->>PH: Build posting effects
    PH->>ACC: Create ledger entries
    PH->>OR: Create OR movements
    PH->>RR: Create RR movements
    ACC->>PG: Append entries
    OR->>PG: Append movements
    RR->>PG: Append movements
    DOC->>PG: Persist posting log / status / links
    PG-->>RT: Commit
    RT-->>UI: Posted result + explainability surfaces`
</script>

<MermaidDiagram :chart="flowchart" />

## Design goals of the posting model

The posting model is intentionally optimized for:

- deterministic behavior;
- idempotency;
- explicit corrections;
- post-fact explainability;
- strong auditability;
- high confidence in financial consistency.

That is why NGB prefers append-only effects and reversal flows over silent in-place mutation of accounting results.

## Main building blocks

### 1. Document lifecycle

A document usually moves through states such as Draft and Posted, with deletion handled as a marked-for-deletion concept rather than immediate physical removal from normal user workflows.

Posting is therefore not a generic save. It is a lifecycle action with consequences.

### 2. Posting validators

Validators make sure a document is allowed to produce effects.

Typical validations include:

- correct status transition;
- period rules;
- required fields;
- balancing rules;
- domain policy constraints;
- anti-duplication or anti-replay checks.

### 3. Posting handlers

A posting handler converts a domain document into business effects.

That handler is where vertical accounting meaning lives. For example, a sales document may:

- debit Accounts Receivable;
- credit Revenue;
- credit Inventory;
- debit Cost of Goods Sold;
- append inventory movements;
- append customer open-item movements.

### 4. Accounting engine

The accounting engine is responsible for ledger semantics, entry validation, and durable append of entries.

### 5. Register engines

Operational and reference register movements are appended alongside ledger entries when the document affects operational balances or reference-state projections.

### 6. Posting log and explainability

The platform keeps enough durable information to answer “was this already posted?” and “what effects did this posting create?”

That is essential for both idempotency and UI explainability.

## Accounting entries as durable facts

Accounting entries are meant to be durable facts rather than mutable current-state rows.

That means a correction is generally expressed as one of the following:

- storno / reversal entries;
- a compensating correction document;
- an explicit repost flow if the platform allows it under controlled rules.

This design greatly improves traceability because the ledger keeps the full story.

## What a posting handler should do

A good posting handler should:

- load only the data it truly needs;
- build entries and movements deterministically;
- avoid hidden side effects;
- produce no-op behavior when nothing should change;
- keep business semantics obvious in code;
- make debugging easy.

A good posting handler should **not**:

- write directly to random tables outside platform abstractions;
- hide business meaning in controller logic;
- mutate prior accounting effects in place;
- depend on UI behavior for correctness.

## Example: business effect shape

A typical sales document in an inventory-aware vertical may produce:

- debit `1100 Accounts Receivable`;
- credit `4000 Product Revenue`;
- debit `5000 Cost of Goods Sold`;
- credit `1200 Inventory`;
- one inventory-out movement per line;
- one receivable open-item movement for the invoice balance;
- document relationships to upstream order or shipment documents if the flow is derived.

## Important implementation mindset

The most important mindset is this:

> **posting code should read like a business explanation, not like database trivia.**

When an auditor or engineer inspects the handler, they should be able to say:

- yes, this is the correct accounting meaning;
- yes, these are the correct operational consequences;
- yes, the platform can explain and reverse this later.

