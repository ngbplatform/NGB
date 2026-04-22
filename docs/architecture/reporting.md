---
title: "Reporting: Canonical and Composable"
---

# Reporting: Canonical and Composable

Reporting is a first-class subsystem in NGB.

The platform supports two complementary reporting styles:

- **Canonical reports** — fixed business or accounting reports with stable semantics and dedicated execution paths;
- **Composable reports** — reusable dataset-driven reports where filters, fields, grouping, sorting, and layout can be composed by the user or by definitions.

These two styles share one broader reporting architecture rather than existing as unrelated subsystems.

## Why NGB needs both

Business software needs both kinds of reports.

### Canonical reports

Some reports are important enough that they should have a controlled, domain-specific meaning:

- Trial Balance
- Balance Sheet
- Income Statement
- General Journal
- Account Card
- Receivables Aging
- Open Items

These are not just tables. They carry accounting or business semantics that should stay stable.

### Composable reports

Other reports are exploratory or presentation-driven:

- sales analysis by customer and month;
- inventory analysis by item and warehouse;
- project hours by manager and billing status;
- lease analytics by property and period.

These benefit from a flexible layout engine.

## Shared reporting principles

Regardless of style, NGB reporting aims for:

- provider-backed execution;
- typed filters and grouping;
- user-facing labels;
- drilldowns and navigation;
- export support;
- consistent UI conventions;
- performance-conscious read paths.

## Canonical reports

A canonical report is usually the right choice when:

- the business meaning must remain fixed;
- running balances or special accounting semantics matter;
- the output requires domain-specific row shaping;
- the report needs a bespoke execution path for correctness or performance.

Examples from the accounting side include the canonical accounting family, where some reports are cursor-paged and some are intentionally bounded and fully rendered.

## Composable reports

A composable report is usually the right choice when:

- the dataset is reusable;
- the user needs control over fields, grouping, and sorting;
- the report should share one planner/executor path with other similar reports;
- the output is more analytical than strictly standardized.

Composable reports are powerful because the platform can reuse one engine for many business questions.

## Key architectural ideas

### 1. Definitions drive behavior

The definition declares:

- code and label;
- filters;
- field metadata;
- grouping options;
- sorting rules;
- dataset or executor binding;
- presentation hints.

### 2. Execution is planned

The engine resolves the requested layout into an execution plan rather than hardcoding one query per UI state.

### 3. Output is UI-ready

The engine returns a report sheet that already knows about:

- rows and cells;
- typed cell actions;
- totals and subtotals;
- formatted values;
- column behavior.

### 4. Drilldowns are part of the design

Reports are not dead exports. They are navigation surfaces.

A document cell should be able to open the document. An account cell should be able to open the Account Card. A grouping path should be explainable.

## Canonical vs composable is not a quality ranking

Composable is not “less serious,” and canonical is not “old-fashioned.”

They solve different problems:

- canonical protects business meaning;
- composable maximizes reuse and exploration.

The strongest reporting platforms need both.

## Performance rules that matter

Reporting can become expensive quickly. NGB therefore leans toward:

- backend-shaped results;
- cursor paging where semantics allow it;
- avoiding UI-side N+1 hydration;
- durable indexes for the actual access path;
- keeping canonical semantics correct even when the implementation differs from a generic dataset path.

## When to choose which

Choose **canonical** when correctness and report meaning are the priority.

Choose **composable** when flexibility and reuse are the priority.

A good platform does not force everything into one model. NGB intentionally supports both.
