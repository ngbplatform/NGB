# Custom document actions

Target: NGB 3.4.0. The [deferral example](../examples/equipment-inspection/EquipmentInspection.Runtime/InspectionDeferralDefinitions.cs)
contributes `example.defer-one-day` through `IDocumentActionDefinitionsContributor`.
It advances a draft inspection's date by one day when equipment is selected,
preserves its number and status, and requires a reason. This is application policy,
not a new SDK capability or a change to standard posting.

Register the contributor and every concrete handler/evaluator type in DI, as well
as the context enricher under `IDocumentActionContextEnricher`. The metadata binds
the exact types. Standard lifecycle actions already come from the document's
handler bindings; do not register them a second time alongside a custom action.

## Authorization and availability

A custom non-derivation action without an authorization evaluator defaults to the
document's view permission in 3.4.0. That default does not express permission to
mutate. `InspectionDeferralAuthorization` explicitly requires both source view and
`edit_draft` through `DocumentActionSecurityContext.Has`. This preserves active
administrator semantics and rejects inactive/unauthenticated contexts. Use the
registered permission codes; never infer administrator access from display names.

An authorization evaluator replaces the default decision for that action, so it
must include the relevant source and operation permissions. Omitted actions in
editor state and rejected dispatcher calls enforce the same rule. Do not use a
disabled button as an authorization boundary.

Authorization and availability evaluators are pure and cannot declare constructor
dependencies. Load bounded provider-neutral facts in the one registered context
enricher for the document type. The example checks source view before reading one
inspection and returns only the equipment-selected boolean. Availability consumes
that fact and returns explicit reasons for non-Draft status, missing equipment or
a date that cannot be advanced. It performs no repository or network calls.

An editor-state query loads one fact set. An executed custom action may load the
old state and the refreshed state separately; the example verifies two bounded
reads for execution, not a false promise of one read for the entire request.
Completed idempotent replay returns its saved result without rerunning the handler
or fact loading. Existing replay observations are recorded separately in
`known-limitations.json`; this example does not change that SDK behavior.

## Transaction and verification

The handler receives the locked document and joins the dispatcher's transaction.
`IDocumentDraftService.UpdateDraftAsync(..., manageTransaction: false)` retains the
number, advances the UTC date and enforces draft-only mutation. Do not start a
nested transaction, commit independently, update lifecycle status directly or send
external messages from the handler. The dispatcher then increments the document
version, writes its action audit including the reason and correlation, and appends
the completion event atomically.

The [HTTP scenarios](../examples/equipment-inspection/EquipmentInspection.IntegrationTests/InspectionDeferralHttpTests.cs)
exercise real PostgreSQL, Keycloak and public packages. They verify the required
reason, exact one-day change, retained Draft/number, audit attribution, one mutation
under concurrent expected-version requests, and an unchanged result on replay.
A user with view alone can read the document but cannot invoke the action. Disabled
business prerequisites also reject direct HTTP calls. A test handler observes the
updated date inside the transaction before throwing; independent reads then verify
that date, version, audit and execution all rolled back and the same request/key
can succeed on retry. Unit cases cover cancellation, missing facts, the date limit
and inactive/unauthenticated contexts without substituting for provider acceptance.
