---
title: Add a Canonical Report
---

# Add a Canonical Report

This guide describes the recommended workflow for adding a **canonical report**.

The example uses an American-market report: **Customer Statement**.

Canonical reports are the right choice when the report needs specialized business logic, custom paging semantics, or a purpose-built read model that should not be expressed as a generic dataset query.

<div class="doc-status-row">
  <span class="doc-badge doc-badge-verified">Verified: runtime reporting anchors</span>
  <span class="doc-badge doc-badge-template">Template: implementation workflow</span>
</div>

<div class="doc-reading-box">
  <p><strong>How to read this page.</strong> Use the verified anchors to understand the shared reporting boundary first, then apply the workflow as a recommended way to implement a domain-specific canonical report.</p>
</div>

## Verified anchors behind this guide

- `NGB.Runtime/Reporting/ReportEngine.cs`
- `NGB.Runtime/Reporting/ReportExecutionPlanner.cs`
- `NGB.PropertyManagement.Api/Program.cs`

## 1. Recognize the canonical use case

Choose a canonical report when one or more of the following is true:

- the report needs opening/closing balance semantics that are awkward in a generic dataset;
- the report needs domain-specific row shaping;
- the report needs custom cursor or running-balance behavior;
- the report has a specialized read path that should stay hand-controlled.

The verified planner source shows an important distinction: when no dataset is involved, the planner builds **canonical predicates** from the report filter metadata. That is the architectural signal that canonical reports are still inside the shared reporting pipeline, but not inside the composable dataset path.

## 2. Define the report contract first

**Template guidance**

Create the report definition for something like `ar.customer_statement`.

Define:

- report code;
- title and presentation metadata;
- filters such as:
  - `customer_id`
  - `from_date`
  - `to_date`
  - `currency_code`
- paging policy;
- typed drilldown or interaction expectations.

Because `ReportEngine` always starts by resolving the definition and validating the request, the definition is the front door of the report.

## 3. Implement the canonical execution path

**Template guidance grounded by verified runtime flow**

The verified `ReportEngine` anchor proves that runtime always delegates execution through a plan executor. For canonical reports, the execution path should therefore be registered as part of the shared reporting engine, not implemented as a parallel API endpoint.

Recommended structure:

- a canonical report service or executor that reads the business source data;
- a mapper that turns source rows into report rows or a ready sheet model;
- interaction metadata for documents, accounts, customers, or periods;
- deterministic paging semantics.

## 4. Keep paging semantics explicit

Canonical reports often fail when paging is bolted on too late.

Decide early whether the report should be:

- bounded and non-paged;
- offset-paged;
- cursor-paged;
- opening-balance aware.

The verified `ReportEngine` source shows that paging behavior is a first-class concept in report execution, not an afterthought. Use that intentionally.

## 5. Prefer purpose-built reads over generic overreach

A canonical report exists because the business semantics matter more than generic reuse.

For a `Customer Statement`, common purpose-built needs include:

- opening balance before the selected period;
- chronological running balance;
- grouping by invoice/payment/apply event types;
- direct drilldowns to source documents.

If these semantics start becoming awkward in a dataset definition, keep the report canonical.

## 6. Surface interactions through the shared report model

Even for canonical reports, the output should still fit the platform reporting surface:

- sheet rows and columns;
- typed document or account interactions;
- totals and subtotals where appropriate;
- exportability.

The verified runtime engine shows that the final response is still normalized through the shared report response path.

## 7. Compose it through the host, not through a one-off route

**Verified anchor:** `NGB.PropertyManagement.Api/Program.cs`

A new canonical report should appear because the vertical module is composed into the host, not because the host gained bespoke business logic.

## 8. Test matrix for a canonical report

At minimum, cover:

- definition resolution;
- filter validation;
- empty-result behavior;
- paging semantics;
- opening-balance correctness when applicable;
- row ordering stability;
- drilldown targets;
- export sheet behavior.

## 9. Production checklist

Before shipping a canonical report, confirm:

- the business semantics are explicit and tested;
- pagination strategy is deliberate;
- totals/running balances are deterministic;
- drilldown behavior is stable;
- the report still participates in the shared reporting UX.

## Read next

- [Reporting Execution Map](/platform/reporting-execution-map)
- [Add a Composable Report](/guides/add-composable-report-workflow)
