---
name: ngb-documents
description: Explain standard NGB document posting or implement document definitions, validation, actions and register effects in an application.
---

## Standard Post

Standard Post requires a registered Accounting, OperationalRegisters or
ReferenceRegisters handler. After validation, a handler that emits no records
can move a Draft to `Posted` with no register movements. If all handlers are
absent, automatic registration omits Post. If the application explicitly exposes
the action anyway, executing it returns `doc.posting.handler.not_configured`. The
document stays in `Draft` and its effects stay unchanged.

## Implementing a document

1. Read [document contracts]({{references}}/documents.md). Inspect the installed
   `NGB.Platform.Definitions` and application definitions, validators and storage.
2. Define head/part metadata and an immutable migration. Keep fields nullable where
   incomplete Drafts are valid; attach business completion checks with
   `AddPostValidator<T>()`, not an unconditional draft check.
   Map `ColumnType.String` to PostgreSQL `text`, including bounded fields; use a
   `CHECK` for the database length limit. `varchar(n)` fails SDK schema validation.
   For bounded text, metadata `MaxLength` does not enforce the Draft HTTP limit.
   Implement both methods of `IDocumentDraftPayloadValidator` in Runtime and
   register it by that interface in DI. Follow the linked reference's
   [payload validation example]({{references}}/documents.md#draft-text-limits).
3. Choose effects according to the application's business rules. For a workflow
   with no register movements, bind the empty handler in Definitions and register
   its implementation in DI. Let the standard action validate and change status.
4. Route interactive actions through the authorized action dispatcher. Preserve
   idempotency keys, expected versions and the existing action availability rules.
   Reload the version after Draft saves. Check the registry date explicitly:
   generic payload dates are not automatically bound to it in 3.4.0, and generic
   save has no expected-version check. Read the documented limitations first.
5. Test incomplete Draft, invalid Post, valid Post, retry, concurrent change and
   unauthorized action against the actual service/API path. Assert the resulting
   effects and preserved history independently of implementation details.
   For replay, use `InspectionRequests.AssertEquivalentJsonAsync` from the example
   to compare the complete response even when object properties change order.
   When adding a Post requirement, update `CompleteInspectionPayload` and all
   positive setup paths that depend on it, including reports, batch posting and
   typed-storage guards. Preserve explicit incomplete Drafts and frozen migration
   setup; do not fill required fields automatically in generic HTTP/action helpers.
   Validate the migrated physical schema against metadata before lifecycle tests;
   follow [schema validation]({{references}}/persistence.md#typed-schema-validation-and-posted-document-guards).
6. Run `node infrastructure/ngb.mjs validate --profile build`,
   `node infrastructure/ngb.mjs test` and
   `node infrastructure/ngb.mjs test --profile integration`.

Done means the requested lifecycle and effect outcomes are proven. A validator
unit test alone is not evidence of persistence, authorization or successful Post.
