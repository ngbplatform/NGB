---
name: ngb-documents
description: Model NGB documents, draft and posting validation, actions and effect combinations through public definitions and services.
---

Use this when implementing or changing document behavior in an external app.
Existing product business rules are application-owned; this skill does not define
them. Confirm the requested outcome and existing contracts first.

1. Read [document contracts](../../references/documents.md). Inspect the installed
   `NGB.Platform.Definitions` and application definitions, validators and storage.
2. Define head/part metadata and an immutable migration. Keep fields nullable where
   incomplete Drafts are valid; attach business completion checks with
   `AddPostValidator<T>()`, not an unconditional draft check.
3. Define the document's intended lifecycle and effects. Standard Post requires a
   registered Accounting, OperationalRegisters or ReferenceRegisters handler, but
   does not require it to produce movements. For a document whose completion has
   no register effects, explicitly register a workflow handler that emits no
   records and retain Post validation. With no handler at all, Post is rejected.
   Never invent movements or write a Posted status directly.
4. Route interactive actions through the authorized action dispatcher. Preserve
   idempotency keys, expected versions and the existing action availability rules.
5. Test incomplete Draft, invalid Post, valid Post, retry, concurrent change and
   unauthorized action against the actual service/API path. Assert the resulting
   effects and preserved history independently of implementation details.
6. Run `node infrastructure/ngb.mjs validate --profile build`,
   `node infrastructure/ngb.mjs test` and
   `node infrastructure/ngb.mjs test --profile integration`.

Done means the requested lifecycle and effect outcomes are proven. A validator
unit test alone is not evidence of persistence, authorization or successful Post.
