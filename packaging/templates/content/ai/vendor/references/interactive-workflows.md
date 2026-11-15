# Interactive document workflows

The runnable [quoted Lines editor](../examples/equipment-inspection/web/src/QuotedLinesEditor.vue)
and its [browser component tests](../examples/equipment-inspection/web/tests/unit/quoted-lines.spec.ts)
demonstrate defaults, date/quantity recalculation, stored-value hydration, readonly
controls and cancellation by row identity. The injected `quote` callback represents
the application's authorized quote endpoint. Amounts use integer minor units and
whole quantities in this bounded example; fractional quantities require a suitable
exact-decimal calculation contract. This component is not a claim of end-to-end
document persistence or server authorization; generated applications must wire and
test those boundaries separately using the acceptance sequence below.

Also use the executable [derived document editor](../examples/equipment-inspection/web/src/DerivedLinesEditor.vue)
and [browser scenarios](../examples/equipment-inspection/web/tests/unit/derived-lines.spec.ts)
for source replacement/clearing, a two-stage calculation, safe HTTP failure messages,
retry and reference navigation. [Reference hydration](../examples/equipment-inspection/web/src/LineReferenceSummary.vue)
demonstrates a header total based on auxiliary data for **all** saved and newly added
rows, without modifying stored prices. [Price adjustment](../examples/equipment-inspection/web/src/PriceAdjustment.vue)
and its [browser test](../examples/equipment-inspection/web/tests/unit/price-adjustment.spec.ts)
demonstrate an editable reason, conditional validation and readonly permissions.
These are application-owned patterns with injected service boundaries, not new SDK
APIs or proof of backend authorization, persistence or full application acceptance.

Derive field behavior from the business process before composing metadata forms
and `NgbDocumentLines`. Generate an application-owned dependency matrix:

| Field category | Source and behavior |
| --- | --- |
| User input | Editable in the permitted draft state; validated on the server |
| Default | Assigned when its inputs become available; preserve a deliberate override where the business permits one |
| Source snapshot | Copied from the selected authorized source, with its effective date and identity; frozen when the transaction is confirmed |
| Calculated | Recomputed when an input changes; visible with its meaning; never left blank without loading/error/empty feedback |
| System identity | Server-owned number, date binding and display; not an arbitrary editable Description |

For every dependency record its trigger, authorized source/read endpoint, affected
fields, invalidation rules, edit permissions, persistence shape and authoritative
server validation. Implement both head-to-lines and lines-to-head dependencies.
Do not label fields readonly merely because implementing their editor is difficult.

## Lookup and calculation behavior

Choosing a parent fills dependent identity fields and limits child lookups to
eligible records. Changing the parent invalidates incompatible children. Choosing
an item requests its applicable value for the selected business date and fills
the source value, agreed value and pricing date where required. A missing value
is an explicit error; zero is not a substitute for an absent approved value.

Use the catalog's business Name as its primary Display unless the business requires
another representation. Keep ID as identity. Put codes, tags and other disambiguators
in secondary lookup metadata, not an invented `code + name` primary display. Reuse
the same presentation in catalog, lookup, line, report and document-flow readers.
For navigable references, wire the lookup's `showOpen` and `open` callback to the
authorized target by ID. Retain navigation in readonly Lines; readonly data does
not mean disabled navigation. Do not render inert plain text or an Open button
without a target. Apply `variant="grid"` to line controls and a `min-h-8`, vertically
centered text container to calculated cells; test mixed controls and multiline rows.

Load dependent values for unsaved drafts through an authorized read/calculation
endpoint accepting the necessary inputs. Do not require Save solely to obtain a
quote. Keep definitive calculations and completeness checks on the server; a
frontend preview cannot authorize a price override or establish final amounts.
Serialize money using the application's exact monetary contract and rounding
policy, not unrestricted binary floating-point arithmetic.

Use stable row identities, never a captured array index, for asynchronous results.
Abort superseded requests and also verify a request generation/input key before
applying a response: cancellation alone does not prove that an old result cannot
arrive. Clear stale dependent values immediately. Removing a row, changing its
item/date, loading a different document or leaving the editor invalidates pending
work. Report failures beside the affected field and allow retry.

Treat a source-dependent header, rows, fees/taxes and totals as one dependency
chain. Clear all of them on source replacement or clearing, before awaiting a
request. A rejected replacement must not leave the previous source's values on
screen or in the save payload. Use the same generation/lifetime through every
asynchronous stage. Late successes **and failures** must be ignored after clear,
replacement, readonly transition, document reload or unmount. Filter lookup choices
on the server for the operation's eligibility; revalidate on calculation and Post
because availability can change after lookup.

Defaults, user overrides and authoritative snapshots have different lifetimes.
Reopening a saved document must hydrate its stored values without repricing it.
Recalculating unrelated totals must not erase an authorized discount and reason.
Changing a source input must not silently retain values from the previous source.
Posted documents remain immutable under the business correction workflow.

Saved snapshots and auxiliary reference data are different. On reopen, load any
reference attributes needed to calculate derived headers for every persisted row.
Do not depend on a cache filled only when the user selects a new item. Add/change/
remove after reopen must include both old and new rows; missing reference data must
produce loading/error feedback, never a silently stale or zero total. Hydrating
auxiliary attributes must not reprice stored agreements or erase overrides.

Explain conditional editability next to the control. Where a reason is required
only for a discount, allow authorized draft users to type it before lowering the
price; require it when confirming the discount. Do not silently discard keystrokes
or make the user discover an unexplained disabled condition. Validate adjustment
permission and reason on the server as well as in the UI.

## Derived documents

A document derived entirely from earlier confirmed transactions may correctly
have readonly lines, source identities, durations, taxes and totals. Keep permitted
draft inputs editable, populate derived fields when the source becomes available,
and explain why computed fields cannot be typed directly. Structural row editing
is a separate permission from editing a particular field.

An explicit Calculate action is appropriate for a business-requested batch or
confirmation step. It is not an automatic replacement for interactive defaults.
Do not introduce a mandatory Save → Calculate → Reload sequence unless the business
workflow requires it and the UI makes the state clear. Never change the business
specification to excuse a missing interaction.

## Browser acceptance

Prepare catalogs, approved source values and identities through test APIs. Then
perform the scenario under test with actual browser controls:

1. Create a draft, choose a parent and add a line. Assert defaults before saving.
2. Change the item and business date. Assert applicable values and recalculated
   totals, including missing values and an intentionally delayed stale response.
3. Exercise allowed overrides and denied changes with the relevant employee role.
4. Reorder/remove rows and verify each remaining value belongs to the correct row.
5. Save, reopen and verify persisted values without an unintended recalculation.
   Then add/change/remove a row and verify all derived headers include saved rows.
6. Confirm through the real action control, inspect the resulting effects and
   follow the actual report/flow link to the expected source.

For each derived document, also select a valid source, replace it with another,
exercise a rejected source, clear it, change the date, and retry a failure. Assert
the customer, rows and every total, not just the selected source. Delay each stage
in turn and resolve/reject it after replacement/clear. Save and reopen the final
state. For each reference column, open its target from both draft and readonly
views, and verify the target ID and denied-access behavior.

Exercise dropdowns in scrolled Lines with mouse, ArrowUp, ArrowDown, Enter and Space.
Check the actual popup bounds relative to its field and the viewport, then choose
an option. A test that checks only `aria-expanded` misses misplaced popups.
Use real shared controls and their styles; replacing Lookup/Select/Input/Lines
with stubs cannot establish editing, focus, geometry or navigation behavior.

Record browser outcomes separately from API and component tests. A readonly
computed field passing a unit assertion does not prove that the user can populate
the document. A successful backend calculator test does not prove its UI is wired.
