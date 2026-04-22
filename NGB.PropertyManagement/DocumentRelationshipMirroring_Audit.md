# Property Management Document Relationship Mirroring Audit

## Purpose

This file records the current `Document Flow` policy for the `Property Management` vertical after the mirrored relationships rollout.

Rule:

> If a typed document reference field expresses a business flow / provenance relationship
> that must be visible in `Document Flow`, then that relationship must be either:
> 1. declaratively mirrored into `document_relationships`, or
> 2. explicitly materialized by runtime logic as an explicit persisted relationship.

Context/document references that are needed only for business context and are not flow edges
must not be automatically added to the graph.

---

## 1. Declarative mirrored relationships (current)

### `pm.work_order`
- `request_id` -> `created_from`
- Reason: a work order is created from a maintenance request, and this is a clean provenance chain.

### `pm.work_order_completion`
- `work_order_id` -> `created_from`
- Reason: a completion is created from a work order, and this is also a clean provenance chain.

---

## 2. Explicit persisted relationships (remain as-is)

These cases are **not moved to mirrored field**, because they already materialize persistent graph edges correctly through runtime/document workflow and/or express multi-edge semantics.

### `pm.receivable_apply`
- `credit_document_id` -> explicit `based_on`
- `charge_document_id` -> explicit `based_on`
- Reason: an apply document forms two directed flow relationships at once. This is not a simple single-field provenance case.

### `pm.payable_apply`
- `credit_document_id` -> explicit `based_on`
- `charge_document_id` -> explicit `based_on`
- Reason: same as receivable apply — multi-edge apply flow.

### `pm.receivable_returned_payment`
- `original_payment_id` -> explicit `based_on`
- Reason: the relationship is created as part of the posting / explainability semantics of the original payment item.

### `pm.work_order_completion`
- explicit `based_on` to the target work order on first post remains **in addition to** mirrored `created_from`
- Reason: `created_from` reflects the provenance typed ref, while `based_on` remains the execution / explainability edge of the current posting workflow.

---

## 3. Document refs intentionally NOT mirrored

Below are typed document refs that remain business context refs and must not generate graph edges by themselves:

- `pm.rent_charge.lease_id`
- `pm.receivable_charge.lease_id`
- `pm.late_fee_charge.lease_id`
- `pm.receivable_payment.lease_id`
- `pm.receivable_credit_memo.lease_id`

Reason: in these cases the lease is context / source-of-data, not a separate flow step. Automatically mirroring such references would add noise to the graph and mix provenance with context navigation.

---

## 4. Current PM decision matrix

### Mirror now
- `pm.work_order.request_id` -> `created_from`
- `pm.work_order_completion.work_order_id` -> `created_from`

### Keep explicit as-is
- `pm.receivable_apply.credit_document_id` -> `based_on`
- `pm.receivable_apply.charge_document_id` -> `based_on`
- `pm.payable_apply.credit_document_id` -> `based_on`
- `pm.payable_apply.charge_document_id` -> `based_on`
- `pm.receivable_returned_payment.original_payment_id` -> `based_on`
- `pm.work_order_completion` first-post `based_on` -> target work order

### Do not mirror
- all remaining PM typed refs that only provide context or lookup narrowing

---

## 5. Maintenance rule for future PM documents

For every new document-to-document field, an explicit decision must be made:

1. **Mirror** — if this is a single-field provenance / flow relationship.
2. **Explicit persisted relationship** — if the flow involves several target documents or is already created by runtime/workflow semantics.
3. **Do not mirror** — if this is only a context/reference field.

A business-significant document reference must not be silently left as only an FK / typed head field without persisted graph representation.
