---
title: Guide · Add Canonical and Composable Reports
---

# Guide · Add Canonical and Composable Reports

This guide shows how to add two different report types in a vertical solution:

1. a **Canonical** report;
2. a **Composable** report.

The point is not that one replaces the other. The point is knowing when to use which.

## Example 1: Canonical report — Accounts Receivable Aging

Aging has stable business meaning and often needs dedicated semantics such as:

- aging buckets;
- as-of date handling;
- open-item logic;
- totals by bucket and customer.

That makes it a strong candidate for a canonical report.

### What to create

```text
NGB.Trade.Definitions/
  Reports/
    TrdReceivablesAgingReportDefinition.cs

NGB.Trade.Runtime/
  Reporting/
    ReceivablesAgingCanonicalReportExecutor.cs
```

### Definition sketch

```csharp
public sealed class TrdReceivablesAgingReportDefinition : ReportDefinition
{
    public const string Code = "trd.receivables.aging";

    public override string ReportCode => Code;
    public override string DisplayName => "Accounts Receivable Aging";
    public override ReportExecutionMode ExecutionMode => ReportExecutionMode.Canonical;
}
```

### Executor responsibilities

The executor should:

- read open receivable state;
- calculate bucket assignment;
- shape totals;
- return typed cell actions for customer and document drilldowns.

## Example 2: Composable report — Sales Analysis

Sales analysis is a better candidate for a composable report because users may want to vary:

- fields;
- grouping;
- sorting;
- time grain.

Typical groupings:

- customer;
- item;
- month;
- warehouse;
- sales rep.

### What to create

```text
NGB.Trade.Definitions/
  Reports/
    TrdSalesAnalysisReportDefinition.cs
```

### Definition sketch

```csharp
public sealed class TrdSalesAnalysisReportDefinition : ReportDefinition
{
    public const string Code = "trd.sales.analysis";

    public override string ReportCode => Code;
    public override string DisplayName => "Sales Analysis";
    public override ReportExecutionMode ExecutionMode => ReportExecutionMode.Composable;
}
```

The composable definition should expose:

- filters;
- available fields;
- allowed grouping;
- sorting metadata;
- time-grain support;
- typed cell actions.

## Report design checklist

Before creating a report, answer:

1. is the meaning fixed or exploratory?
2. do we need custom balance or running-total semantics?
3. will users change layout often?
4. is cursor paging needed?
5. which cells should drill into documents or reports?

## Cell actions

Reports in NGB should not return dead strings where navigation matters.

Examples:

- customer cell → open customer catalog entity;
- document cell → open document;
- account cell → open Account Card;
- property cell → open property.

This keeps reporting integrated with the rest of the platform.

## UI expectations

A production-ready report should return UI-friendly output that already understands:

- labels;
- totals;
- grouping;
- sorting metadata;
- cell actions;
- export shape.

## When to prefer canonical

Prefer canonical when:

- accounting semantics are special;
- the report has a standard meaning;
- the row model is not just “select fields and group.”

## When to prefer composable

Prefer composable when:

- the dataset is reusable;
- layout flexibility is important;
- one engine can serve many report shapes.

## Final checklist

- report code and label are stable;
- filters and fields are user-friendly;
- drilldowns are wired;
- performance path matches report semantics;
- the report is tested with realistic data.

A good reporting subsystem does not force everything into one template. NGB is strongest when canonical and composable reports are used deliberately.
